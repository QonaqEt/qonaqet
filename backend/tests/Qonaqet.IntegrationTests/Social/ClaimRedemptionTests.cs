using System.Net;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.IntegrationTests.Businesses;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Persistence.Mongo;
using static Qonaqet.IntegrationTests.Social.SocialTestData;

namespace Qonaqet.IntegrationTests.Social;

/// <summary>Claim-in işçi tərəfindən təhvili (E06-T05).</summary>
[Collection(MongoCollection.Name)]
public sealed class ClaimRedemptionTests(MongoFixture mongo)
{
    [Fact]
    public async Task Staff_previews_and_redeems_by_qr_or_short_code_and_a_repeat_is_already_redeemed()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var shop = await ShopAsync(host);
        var student = await CategoryAsync(host, "student");
        var tea = Id(await DailyAsync(host, shop, student, quota: 5));
        var first = await host.LoginAsync("student1@example.az");
        var second = await host.LoginAsync("student2@example.az");
        await VerifyAsync(host, "student1@example.az", student);
        await VerifyAsync(host, "student2@example.az", student);

        var byQr = await ClaimOkAsync(host, first, tea);
        var qr = byQr.GetProperty("codes").GetProperty("qrPayload").GetString()!;
        var preview = await PostOkAsync(host, shop, "redeem/preview", qr);
        Assert.Equal("Claim", preview.GetProperty("kind").GetString());
        Assert.Equal("Çay", preview.GetProperty("title").GetString());
        Assert.Equal("Reserved", preview.GetProperty("status").GetString());
        Assert.Equal("Tələbələr", preview.GetProperty("label").GetString());
        Assert.True(preview.GetProperty("canRedeem").GetBoolean());
        Assert.Equal(JsonValueKind.Null, preview.GetProperty("amountQepik").ValueKind);

        var redeemed = await PostOkAsync(host, shop, "redeem", qr);
        Assert.Equal("Redeemed", redeemed.GetProperty("status").GetString());
        Assert.False(redeemed.GetProperty("canRedeem").GetBoolean());

        using (var again = await PostAsync(host, shop.Staff, shop.BusinessId, "redeem", qr))
        {
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
            Assert.Equal("already_redeemed", await CodeAsync(host, again));
        }

        // Qısa kod: kiçik hərflə və defissiz yazılsa da tanınır.
        var byCode = await ClaimOkAsync(host, second, tea);
        var shortCode = byCode.GetProperty("codes").GetProperty("shortCode").GetString()!.Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
        Assert.Equal("Redeemed", (await PostOkAsync(host, shop, "redeem", shortCode)).GetProperty("status").GetString());
        using (var again = await PostAsync(host, shop.Staff, shop.BusinessId, "redeem", shortCode))
        {
            Assert.Equal("already_redeemed", await CodeAsync(host, again));
        }

        var stored = await host.Database.GetCollection<BsonDocument>("claims").Find(new BsonDocument("_id", Bin(Id(byQr)))).SingleAsync(Ct);
        Assert.Equal("Redeemed", stored["status"].AsString);
        Assert.Equal(Bin(UserId(shop.Staff)), stored["redeemedByStaffId"]);

        using var mine = await host.GetAsync("/api/v1/me/claims", Access(first));
        Assert.Equal("Redeemed", (await host.JsonAsync(mine)).EnumerateArray().Single().GetProperty("status").GetString());
    }

    [Fact]
    public async Task Redemption_writes_one_claim_redeemed_event_without_commission_or_recipient()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var shop = await ShopAsync(host);
        var everyone = await CategoryAsync(host, "everyone");
        var tea = Id(await SuspendedAsync(host, shop, everyone, quantity: 3));
        var guest = await host.LoginAsync("guest@example.az");
        var claim = await ClaimOkAsync(host, guest, tea);
        var qr = claim.GetProperty("codes").GetProperty("qrPayload").GetString()!;

        // İki işçi (və ya iki skan) eyni anda: yalnız biri təhvil verir.
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => PostAsync(host, shop.Staff, shop.BusinessId, "redeem", qr)));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        foreach (var response in responses)
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                Assert.Equal("already_redeemed", await CodeAsync(host, response));
            }

            response.Dispose();
        }

        var events = await host.Database.GetCollection<BsonDocument>("outboxMessages")
            .Find(Builders<BsonDocument>.Filter.Regex("type", new BsonRegularExpression("ClaimRedeemed$"))).ToListAsync(Ct);
        var evt = Assert.Single(events);
        using var payload = JsonDocument.Parse(evt["payload"].AsString);
        var root = payload.RootElement;
        Assert.False(Property(root, "commissionable").GetBoolean());
        Assert.Equal(((int)Qonaqet.Domain.Social.OfferType.Suspended).ToString(System.Globalization.CultureInfo.InvariantCulture), Property(root, "offerType").ToString());
        Assert.DoesNotContain(UserId(guest), evt["payload"].AsString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Codes_from_other_businesses_expired_claims_and_non_members_are_refused()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var shop = await ShopAsync(host);
        var otherShop = await OtherShopAsync(host, shop);
        var everyone = await CategoryAsync(host, "everyone");
        var tea = Id(await DailyAsync(host, shop, everyone, quota: 5));
        var guest = await host.LoginAsync("guest@example.az");
        var claim = await ClaimOkAsync(host, guest, tea);
        var qr = claim.GetProperty("codes").GetProperty("qrPayload").GetString()!;
        var shortCode = claim.GetProperty("codes").GetProperty("shortCode").GetString()!;

        foreach (var code in new[] { qr, shortCode })
        {
            using var foreign = await PostAsync(host, otherShop.Owner, otherShop.BusinessId, "redeem/preview", code);
            Assert.Equal("code_not_found", await CodeAsync(host, foreign));
        }

        using (var garbage = await PostAsync(host, shop.Staff, shop.BusinessId, "redeem", "ZZZZ-ZZZZ"))
        {
            Assert.Equal("code_not_found", await CodeAsync(host, garbage));
        }

        using (var outsider = await PostAsync(host, guest, shop.BusinessId, "redeem", qr))
        {
            Assert.Equal(HttpStatusCode.Forbidden, outsider.StatusCode);
        }

        await host.Database.GetCollection<BsonDocument>("claims").UpdateOneAsync(
            new BsonDocument("_id", Bin(Id(claim))),
            new BsonDocument("$set", new BsonDocument("expiresAt", host.Clock.UtcNow.AddSeconds(-1).UtcDateTime)), cancellationToken: Ct);
        Assert.Equal("Expired", (await PostOkAsync(host, shop, "redeem/preview", qr)).GetProperty("status").GetString());
        using (var expired = await PostAsync(host, shop.Staff, shop.BusinessId, "redeem", qr))
        {
            Assert.Equal("claim_expired", await CodeAsync(host, expired));
        }
    }

    [Fact]
    public async Task Redeem_endpoints_are_rate_limited()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var shop = await ShopAsync(host);

        host.RateLimits.Deny = true;
        using var response = await PostAsync(host, shop.Staff, shop.BusinessId, "redeem/preview", "ABCD-EFGH");
        host.RateLimits.Deny = false;

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    internal static Task<HttpResponseMessage> PostAsync(ApiTestHost host, JsonElement login, string businessId, string path, string code) =>
        host.SendAsync(HttpMethod.Post, $"/api/v1/business/{businessId}/{path}", Access(login), new { code });

    internal static async Task<JsonElement> PostOkAsync(ApiTestHost host, Shop shop, string path, string code)
    {
        using var response = await PostAsync(host, shop.Staff, shop.BusinessId, path, code);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return await host.JsonAsync(response);
    }

    internal static async Task<Shop> OtherShopAsync(ApiTestHost host, Shop shop)
    {
        var (owner, id) = await BusinessTestData.ActiveBusinessAsync(
            host, shop.Admin, "rival@example.az", BusinessTestData.ApplicationBody("Rəqib", "7777777777"));
        return new Shop(shop.Admin, owner, owner, id);
    }

    private static JsonElement Property(JsonElement root, string name) =>
        root.EnumerateObject().First(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;
}
