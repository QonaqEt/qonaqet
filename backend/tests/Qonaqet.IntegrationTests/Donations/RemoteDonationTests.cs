using System.Net;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Domain.Payments;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Payments;
using Qonaqet.IntegrationTests.Social;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Donations;

/// <summary>Remote ianə (E08-T01): server məbləği, ödəniş, təsdiqdən sonra idempotent aktivləşmə, adlı/anonim göstərim.</summary>
[Collection(PaymentsCollection.Name)]
public sealed class RemoteDonationTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Paid_remote_donation_becomes_a_claimable_offer_exactly_once()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var (shop, itemId, category) = await DonationTestData.SetupAsync(host, "remote", price: 2_50);
        var donor = await host.LoginAsync("remote-donor@example.az");

        // Klientin göndərdiyi məbləğ nəzərə alınmır: qiymət kataloqdan, say × qiymət.
        using var start = await PostIdempotentAsync(host, "/api/v1/donations/remote", Access(donor), new
        {
            businessId = shop.BusinessId, catalogItemId = itemId, categoryId = category, quantity = 3, donorDisplay = "Named", donorName = "Leyla", amountQepik = 1,
        });
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        var started = await host.JsonAsync(start);
        Assert.Equal(7_50, started.GetProperty("amountQepik").GetInt64());
        Assert.False(started.GetProperty("refundable").GetBoolean());
        var offerId = started.GetProperty("donationId").GetString()!;

        Assert.DoesNotContain(await PublicOfferIdsAsync(host, category), id => id == offerId); // ödənilməyib → görünmür

        await PayAsync(host, started.GetProperty("paymentUrl").GetString()!);
        var paid = await LoadAsync(host, PaymentId.From(started.GetProperty("paymentId").GetGuid()));
        Assert.Equal(PaymentPurpose.Remote, paid.Purpose);
        Assert.Equal(7_50, paid.Amount);

        Assert.Equal(1, await DeliverAsync(host, Fulfillment(host)));
        Assert.Equal(0, await DeliverAsync(host, Fulfillment(host)));
        Assert.Contains(await PublicOfferIdsAsync(host, category), id => id == offerId);

        var offer = await host.Database.GetCollection<BsonDocument>("offers").Find(new BsonDocument("_id", SocialTestData.Bin(offerId))).SingleAsync(Ct);
        Assert.True(offer["isActive"].AsBoolean);
        Assert.Equal(3, offer["remaining"].AsInt32);
        Assert.Equal("Leyla", offer["donorName"].AsString);

        // Alıcı claim edir: məbləğ vahid qiymətidir və ianəçinin adı göstərilir.
        var recipient = await host.LoginAsync("remote-recipient@example.az");
        using var claim = await host.SendAsync(HttpMethod.Post, $"/api/v1/offers/{offerId}/claim", Access(recipient));
        Assert.Equal(HttpStatusCode.Created, claim.StatusCode);
        var claimJson = await host.JsonAsync(claim);
        Assert.Equal("Leyla sizə ısmarladı 💚", claimJson.GetProperty("donorDisplayText").GetString());
        var claimDoc = await host.Database.GetCollection<BsonDocument>("claims").Find(new BsonDocument("recipientUserId", SocialTestData.Bin(UserId(recipient).ToString()))).SingleAsync(Ct);
        Assert.Equal(2_50, claimDoc["amountQepik"].ToInt64());

        // İanəçi öz ianəsini aqreqat kimi görür.
        using var mine = await host.GetAsync("/api/v1/me/donations", Access(donor));
        var donations = (await host.JsonAsync(mine)).EnumerateArray().ToList();
        Assert.Contains(donations, d => d.GetProperty("type").GetString() == "Remote" && d.GetProperty("totalQuantity").GetInt32() == 3);
    }

    [Fact]
    public async Task Failed_payment_closes_the_hidden_offer()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var (shop, itemId, category) = await DonationTestData.SetupAsync(host, "remote-fail");
        var donor = await host.LoginAsync("remote-fail-donor@example.az");
        using var start = await PostIdempotentAsync(host, "/api/v1/donations/remote", Access(donor), new
        {
            businessId = shop.BusinessId, catalogItemId = itemId, categoryId = category, quantity = 1, donorDisplay = "Anonymous",
        });
        Assert.True(start.StatusCode == HttpStatusCode.Created, await start.Content.ReadAsStringAsync(Ct));
        var started = await host.JsonAsync(start);

        await PayAsync(host, started.GetProperty("paymentUrl").GetString()!, paid: false);
        Assert.Equal(1, await DeliverAsync(host, Fulfillment(host)));

        var offer = await host.Database.GetCollection<BsonDocument>("offers")
            .Find(new BsonDocument("_id", SocialTestData.Bin(started.GetProperty("donationId").GetString()!))).SingleAsync(Ct);
        Assert.True(offer["isDeleted"].AsBoolean);
        Assert.False(offer["isActive"].AsBoolean);
    }

    [Fact]
    public async Task Validation_named_without_name_inactive_item_and_missing_key()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var (shop, itemId, category) = await DonationTestData.SetupAsync(host, "remote-val");
        var donor = Access(await host.LoginAsync("remote-val-donor@example.az"));

        using (var noName = await PostIdempotentAsync(host, "/api/v1/donations/remote", donor,
            new { businessId = shop.BusinessId, catalogItemId = itemId, categoryId = category, quantity = 1, donorDisplay = "Named" }))
        {
            Assert.Equal("donor_name_required", (await host.JsonAsync(noName)).GetProperty("code").GetString());
        }

        using (var wrongItem = await PostIdempotentAsync(host, "/api/v1/donations/remote", donor,
            new { businessId = shop.BusinessId, catalogItemId = Guid.CreateVersion7(), categoryId = category, quantity = 1, donorDisplay = "Anonymous" }))
        {
            Assert.Equal("donation_item_unavailable", (await host.JsonAsync(wrongItem)).GetProperty("code").GetString());
        }

        using (var tooMany = await PostIdempotentAsync(host, "/api/v1/donations/remote", donor,
            new { businessId = shop.BusinessId, catalogItemId = itemId, categoryId = category, quantity = 101, donorDisplay = "Anonymous" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        }

        using var noKey = await host.SendAsync(HttpMethod.Post, "/api/v1/donations/remote", donor,
            new { businessId = shop.BusinessId, catalogItemId = itemId, categoryId = category, quantity = 1, donorDisplay = "Anonymous" });
        Assert.Equal(HttpStatusCode.BadRequest, noKey.StatusCode);
    }

    private static async Task<IReadOnlyList<string>> PublicOfferIdsAsync(ApiTestHost host, string categoryId)
    {
        using var response = await host.GetAsync($"/api/v1/offers?categoryId={categoryId}", null);
        return [.. (await host.JsonAsync(response)).EnumerateArray().Select(o => o.GetProperty("id").GetString()!)];
    }
}

/// <summary>İanə testlərinin ortaq hazırlığı: aktiv biznes (hovuz qəbul edir), kataloq məhsulu, kateqoriya.</summary>
internal static class DonationTestData
{
    public static async Task<(Shop Shop, string ItemId, string CategoryId)> SetupAsync(ApiTestHost host, string prefix, long price = 3_00, string category = "everyone")
    {
        var shop = await SocialTestData.ShopAsync(host, prefix);
        using (var settings = await host.SendAsync(HttpMethod.Patch, $"/api/v1/business/{shop.BusinessId}/settings", SocialTestData.Access(shop.Owner), new { acceptsPool = true }))
        {
            Assert.True(settings.IsSuccessStatusCode, await settings.Content.ReadAsStringAsync(Ct));
        }

        using var item = await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{shop.BusinessId}/catalog", SocialTestData.Access(shop.Owner),
            new { title = "Kofe", priceQepik = price, isActive = true, isGiftable = true });
        Assert.True(item.IsSuccessStatusCode, await item.Content.ReadAsStringAsync(Ct));
        var itemId = (await host.JsonAsync(item)).GetProperty("id").GetString()!;
        return (shop, itemId, await SocialTestData.CategoryAsync(host, category));
    }

    public static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;
}
