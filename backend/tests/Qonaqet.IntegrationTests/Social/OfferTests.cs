using System.Net;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Persistence.Mongo;
using static Qonaqet.IntegrationTests.Social.SocialTestData;

namespace Qonaqet.IntegrationTests.Social;

/// <summary>Gündəlik və askıda təkliflər (E06-T01).</summary>
[Collection(MongoCollection.Name)]
public sealed class OfferTests(MongoFixture mongo)
{
    [Fact]
    public async Task Owner_creates_daily_offers_and_staff_records_suspended_ones()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var shop = await ShopAsync(host);
        var student = await CategoryAsync(host, "student");
        var donor = await host.LoginAsync("donor@example.az");

        var daily = await DailyAsync(host, shop, student, extra: new
        {
            categoryId = student, title = "Tələbəyə çay", dailyQuota = 20, availableFrom = "09:00", availableTo = "18:00", donorDisplay = "Named",
        });
        Assert.Equal("BusinessDaily", daily.GetProperty("type").GetString());
        Assert.Equal(20, daily.GetProperty("availableToday").GetInt32());
        Assert.Equal("Tələbələr", daily.GetProperty("categoryLabel").GetString());

        var suspended = await SuspendedAsync(host, shop, student, quantity: 4, donorEmail: "Donor@Example.az", display: "Named", donorName: "Leyla");
        Assert.Equal("Suspended", suspended.GetProperty("type").GetString());
        Assert.Equal(4, suspended.GetProperty("remaining").GetInt32());

        // Staff gündəlik təklif yarada bilməz (Manage), amma askıda qeyd edə bilər (Operate).
        using (var forbidden = await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{shop.BusinessId}/offers", Access(shop.Staff),
            new { categoryId = student, title = "Çay", dailyQuota = 5 }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        var stored = await host.Database.GetCollection<BsonDocument>("offers")
            .Find(new BsonDocument("_id", new BsonBinaryData(Guid.Parse(Id(suspended)), GuidRepresentation.Standard))).SingleAsync(Ct);
        Assert.Equal("Suspended", stored["type"].AsString);
        Assert.Equal(new BsonBinaryData(Guid.Parse(UserId(donor)), GuidRepresentation.Standard), stored["donorUserId"]);
        Assert.Equal(new BsonBinaryData(Guid.Parse(UserId(shop.Staff)), GuidRepresentation.Standard), stored["createdByStaffId"]);
        Assert.False(stored["isDeleted"].AsBoolean);

        using var list = await host.GetAsync($"/api/v1/business/{shop.BusinessId}/offers", Access(shop.Staff));
        Assert.Equal(2, (await host.JsonAsync(list)).GetArrayLength());

        using var publicList = await host.GetAsync($"/api/v1/offers?categoryId={student}", null);
        var offers = (await host.JsonAsync(publicList)).EnumerateArray().ToArray();
        Assert.Equal(2, offers.Length);
        Assert.Contains(offers, o => o.GetProperty("donorText").GetString() == "Leyla ısmarlayır");
        Assert.Contains(offers, o => o.GetProperty("donorText").GetString() == "Çay Evi ısmarlayır");
    }

    [Fact]
    public async Task Inputs_are_validated_and_sensitive_categories_are_labelled_neutrally()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var shop = await ShopAsync(host);
        var veteran = await CategoryAsync(host, "veteran-family");

        foreach (var (body, code) in new (object Body, string Code)[]
        {
            (new { categoryId = veteran, title = "Çay", dailyQuota = 0 }, "daily_quota_invalid"),
            (new { categoryId = veteran, title = " ", dailyQuota = 5 }, "title_invalid"),
            (new { categoryId = veteran, title = "Çay", dailyQuota = 5, availableFrom = "09:00" }, "offer_window_invalid"),
            (new { categoryId = Guid.CreateVersion7(), title = "Çay", dailyQuota = 5 }, "category_not_found"),
            (new { categoryId = veteran, title = "Çay", dailyQuota = 5, catalogItemId = Guid.CreateVersion7() }, "offer_catalog_item_invalid"),
        })
        {
            using var bad = await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{shop.BusinessId}/offers", Access(shop.Owner), body);
            Assert.Contains(code, await bad.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }

        using (var unknownDonor = await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{shop.BusinessId}/offers/suspended", Access(shop.Staff),
            new { categoryId = veteran, title = "Kofe", quantity = 1, donorEmail = "nobody@example.az" }))
        {
            Assert.Equal("donor_not_found", (await host.JsonAsync(unknownDonor)).GetProperty("code").GetString());
        }

        var daily = await DailyAsync(host, shop, veteran);
        Assert.Equal("Qonaq", daily.GetProperty("categoryLabel").GetString());
    }

    [Fact]
    public async Task Owner_updates_and_soft_deletes_offers()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var shop = await ShopAsync(host);
        var everyone = await CategoryAsync(host, "everyone");
        var daily = await DailyAsync(host, shop, everyone);

        using (var update = await host.SendAsync(HttpMethod.Put, $"/api/v1/business/{shop.BusinessId}/offers/{Id(daily)}", Access(shop.Owner),
            new { title = "Şirniyyat", dailyQuota = 10, donorDisplay = "Anonymous", isActive = false }))
        {
            var updated = await host.JsonAsync(update);
            Assert.Equal(10, updated.GetProperty("dailyQuota").GetInt32());
            Assert.False(updated.GetProperty("isActive").GetBoolean());
        }

        using (var hidden = await host.GetAsync($"/api/v1/offers?businessId={shop.BusinessId}", null))
        {
            Assert.Equal(0, (await host.JsonAsync(hidden)).GetArrayLength());
        }

        using (var delete = await host.SendAsync(HttpMethod.Delete, $"/api/v1/business/{shop.BusinessId}/offers/{Id(daily)}", Access(shop.Owner)))
        {
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        using (var list = await host.GetAsync($"/api/v1/business/{shop.BusinessId}/offers", Access(shop.Owner)))
        {
            Assert.Equal(0, (await host.JsonAsync(list)).GetArrayLength());
        }

        var stored = await host.Database.GetCollection<BsonDocument>("offers").Find(FilterDefinition<BsonDocument>.Empty).SingleAsync(Ct);
        Assert.True(stored["isDeleted"].AsBoolean);
    }

    [Fact]
    public async Task Offer_indexes_are_partial()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        using (await host.GetAsync("/api/v1/categories", null))
        {
        }

        var indexes = await (await host.Database.GetCollection<BsonDocument>("offers").Indexes.ListAsync(Ct)).ToListAsync(Ct);
        foreach (var name in new[] { "ix_offers_businessId_type", "ix_offers_categoryId_type" })
        {
            var index = Assert.Single(indexes, i => i["name"].AsString == name);
            Assert.Equal(new BsonDocument("isDeleted", false), index["partialFilterExpression"].AsBsonDocument);
        }
    }
}
