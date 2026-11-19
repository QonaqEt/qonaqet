using System.Net;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Persistence.Mongo;
using static Qonaqet.IntegrationTests.Businesses.BusinessTestData;

namespace Qonaqet.IntegrationTests.Businesses;

/// <summary>Kataloq CRUD və biznesin kaskad soft delete-i (E04-T03).</summary>
[Collection(MongoCollection.Name)]
public sealed class CatalogTests(MongoFixture mongo)
{
    private static object Item(string title = "Çay", long price = 250, string? group = "İsti içkilər", bool giftable = true) =>
        new { title, description = "Armudu stəkanda", priceQepik = price, group, isActive = true, isGiftable = giftable };

    [Fact]
    public async Task Owner_manages_the_catalog_and_deletion_is_soft()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var admin = await RolesTests.LoginAsAdminAsync(host, "admin@example.az");
        var (owner, id) = await ActiveBusinessAsync(host, admin, "owner@example.az");

        var tea = await CreateAsync(host, owner, id, Item());
        var coffee = await CreateAsync(host, owner, id, Item("Kofe", 450, group: null, giftable: false));
        Assert.Equal(250, tea.GetProperty("priceQepik").GetInt64());

        using (var update = await host.SendAsync(HttpMethod.Put, $"/api/v1/business/{id}/catalog/{tea.GetProperty("id").GetString()}", Access(owner),
            new { title = "Çay dəstgahı", priceQepik = 900, group = "İsti içkilər", isActive = true, isGiftable = true }))
        {
            Assert.Equal(900, (await host.JsonAsync(update)).GetProperty("priceQepik").GetInt64());
        }

        var coffeeId = coffee.GetProperty("id").GetString();
        using (var delete = await host.SendAsync(HttpMethod.Delete, $"/api/v1/business/{id}/catalog/{coffeeId}?reason=menyudan", Access(owner)))
        {
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        var titles = await TitlesAsync(host, owner, id);
        Assert.Equal(["Çay dəstgahı"], titles);

        var stored = await host.Database.GetCollection<BsonDocument>("catalogItems")
            .Find(new BsonDocument("_id", new BsonBinaryData(Guid.Parse(coffeeId!), GuidRepresentation.Standard))).SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(stored["isDeleted"].AsBoolean);
        Assert.Equal(450, stored["priceQepik"].AsInt64);
    }

    [Fact]
    public async Task Prices_images_and_permissions_are_checked_on_the_server()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var admin = await RolesTests.LoginAsAdminAsync(host, "admin@example.az");
        var (owner, id) = await ActiveBusinessAsync(host, admin, "owner@example.az");
        var (otherOwner, otherId) = await ActiveBusinessAsync(host, admin, "other@example.az", ApplicationBody("Gül evi", "5555555555", type: "Florist"));
        var staff = await host.LoginAsync("barista@example.az");
        using (var add = await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{id}/staff", Access(owner), new { email = "barista@example.az" }))
        {
            add.EnsureSuccessStatusCode();
        }

        foreach (var price in new long[] { 0, -5, 1_000_001 })
        {
            using var bad = await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{id}/catalog", Access(owner), Item(price: price));
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            Assert.Contains("price_invalid", await bad.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }

        using (var image = await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{id}/catalog", Access(owner),
            new { title = "Çay", priceQepik = 250, imageFileId = Guid.CreateVersion7(), isActive = true, isGiftable = true }))
        {
            Assert.Equal("catalog_image_invalid", (await host.JsonAsync(image)).GetProperty("code").GetString());
        }

        var tea = await CreateAsync(host, owner, id, Item());
        Assert.Single(await TitlesAsync(host, staff, id));

        using (var staffCreate = await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{id}/catalog", Access(staff), Item()))
        {
            Assert.Equal(HttpStatusCode.Forbidden, staffCreate.StatusCode);
        }

        using (var foreign = await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{id}/catalog", Access(otherOwner), Item()))
        {
            Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        }

        // Başqa biznesin elementinə öz biznesinin yolu ilə müraciət: tapılmır.
        using var crossItem = await host.SendAsync(HttpMethod.Put, $"/api/v1/business/{otherId}/catalog/{tea.GetProperty("id").GetString()}", Access(otherOwner), Item(price: 1));
        Assert.Equal("catalog_item_not_found", (await host.JsonAsync(crossItem)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Deleting_a_business_cascades_to_catalog_and_memberships_and_restore_brings_them_back()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var admin = await RolesTests.LoginAsAdminAsync(host, "admin@example.az");
        var (owner, id) = await ActiveBusinessAsync(host, admin, "owner@example.az");
        await CreateAsync(host, owner, id, Item());
        var removed = await CreateAsync(host, owner, id, Item("Köhnə", 100));
        using (var delete = await host.SendAsync(HttpMethod.Delete, $"/api/v1/business/{id}/catalog/{removed.GetProperty("id").GetString()}", Access(owner)))
        {
            delete.EnsureSuccessStatusCode();
        }

        using (var deleteBusiness = await host.SendAsync(HttpMethod.Delete, $"/api/v1/admin/businesses/{id}?reason=bağlandı", Access(admin)))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleteBusiness.StatusCode);
        }

        var items = host.Database.GetCollection<BsonDocument>("catalogItems");
        var cascaded = await items.CountDocumentsAsync(
            new BsonDocument { { "isDeleted", true }, { "deleteReason", "business_deleted" } }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, cascaded);
        Assert.Equal(0, await items.CountDocumentsAsync(new BsonDocument("isDeleted", false), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(1, await host.Database.GetCollection<BsonDocument>("businesses")
            .CountDocumentsAsync(new BsonDocument("isDeleted", true), cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(await EventExistsAsync(host, "BusinessDeleted"));

        using (var gone = await host.GetAsync($"/api/v1/business/{id}/catalog", Access(owner)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, gone.StatusCode);
        }

        owner = await RefreshAsync(host, owner);
        Assert.DoesNotContain("BusinessOwner", await RolesAsync(host, owner));

        using (var restore = await host.SendAsync(HttpMethod.Post, $"/api/v1/admin/businesses/{id}/restore", Access(admin)))
        {
            Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);
        }

        Assert.Equal(["Çay"], await TitlesAsync(host, owner, id));
        owner = await RefreshAsync(host, owner);
        Assert.Contains("BusinessOwner", await RolesAsync(host, owner));
        Assert.True(await EventExistsAsync(host, "BusinessRestored"));
    }

    [Fact]
    public async Task Catalog_has_the_partial_business_active_index()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        await host.LoginAsync("warmup@example.az");

        var indexes = await (await host.Database.GetCollection<BsonDocument>("catalogItems").Indexes.ListAsync(TestContext.Current.CancellationToken))
            .ToListAsync(TestContext.Current.CancellationToken);
        var index = indexes.Single(i => i["name"] == "ix_catalogItems_businessId_isActive");
        Assert.Equal(new BsonDocument { { "businessId", 1 }, { "isActive", 1 } }, index["key"].AsBsonDocument);
        Assert.Equal(new BsonDocument("isDeleted", false), index["partialFilterExpression"]);
    }

    private static async Task<JsonElement> CreateAsync(ApiTestHost host, JsonElement owner, string businessId, object item)
    {
        using var response = await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{businessId}/catalog", Access(owner), item);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return await host.JsonAsync(response);
    }

    private static async Task<string?[]> TitlesAsync(ApiTestHost host, JsonElement member, string businessId)
    {
        using var response = await host.GetAsync($"/api/v1/business/{businessId}/catalog", Access(member));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return [.. (await host.JsonAsync(response)).EnumerateArray().Select(i => i.GetProperty("title").GetString())];
    }

    private static async Task<string?[]> RolesAsync(ApiTestHost host, JsonElement login)
    {
        using var whoami = await host.GetAsync("/__test/auth/whoami", Access(login));
        return [.. (await host.JsonAsync(whoami)).GetProperty("roles").EnumerateArray().Select(r => r.GetString())];
    }
}
