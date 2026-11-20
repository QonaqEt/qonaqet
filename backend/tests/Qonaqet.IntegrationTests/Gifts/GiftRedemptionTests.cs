using System.Net;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Payments;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Gifts;

/// <summary>Hədiyyənin təhvili (E09-T06): QR/qısa kod, preview, qismən təhvil, şəbəkə vauçeri, paralel təhvil, şəxsi məlumatın gizlənməsi.</summary>
[Collection(PaymentsCollection.Name)]
public sealed class GiftRedemptionTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Voucher_is_redeemed_partially_and_staff_never_sees_personal_data()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var shop = await GiftTestData.ShopAsync(host, "gredeem");
        var sender = await host.LoginAsync("gredeem-sender@example.az");
        var (giftId, token) = await GiftTestData.DeliveredAsync(host, sender, GiftTestData.Voucher(shop, 50_00, email: "gredeem-ana@example.az"));
        var codes = await CodesAsync(host, token);

        using (var preview = await PreviewAsync(host, shop, codes.Qr))
        {
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            var text = await preview.Content.ReadAsStringAsync(Ct);
            var body = JsonDocument.Parse(text).RootElement;
            Assert.Equal("Gift", body.GetProperty("kind").GetString());
            Assert.Equal("Hədiyyə", body.GetProperty("label").GetString());
            Assert.Equal(50_00, body.GetProperty("gift").GetProperty("remainingQepik").GetInt64());
            Assert.True(body.GetProperty("canRedeem").GetBoolean());
            foreach (var secret in new[] { "Ana", "gredeem-ana", "gredeem-sender", "Səni sevirəm", UserId(sender).ToString() })
            {
                Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
            }
        }

        var first = await RedeemOkAsync(host, shop, codes.Short, new { amountQepik = 20_00 });
        Assert.Equal(20_00, first.GetProperty("gift").GetProperty("redeemedNowQepik").GetInt64());
        Assert.Equal(30_00, first.GetProperty("gift").GetProperty("remainingQepik").GetInt64());
        Assert.Equal("PartiallyRedeemed", first.GetProperty("status").GetString());

        Assert.Equal("gift_insufficient_balance", await RedeemErrorAsync(host, shop, codes.Qr, new { amountQepik = 30_01 }));
        Assert.Equal("gift_selection_invalid", await RedeemErrorAsync(host, shop, codes.Qr, new { amountQepik = 0 }));
        Assert.Equal("gift_selection_invalid", await RedeemErrorAsync(host, shop, codes.Qr, new { }));

        var last = await RedeemOkAsync(host, shop, codes.Qr, new { amountQepik = 30_00 });
        Assert.Equal("Redeemed", last.GetProperty("status").GetString());
        Assert.False(last.GetProperty("canRedeem").GetBoolean());
        Assert.Equal("already_redeemed", await RedeemErrorAsync(host, shop, codes.Qr, new { amountQepik = 1 }));

        var doc = await GiftTestData.GiftDocAsync(host, giftId);
        Assert.Equal((0L, 50_00L, "Redeemed"), (doc["remainingQepik"].ToInt64(), doc["redeemedQepik"].ToInt64(), doc["status"].AsString));
        var redemptions = await RedemptionsAsync(host, giftId);
        Assert.Equal([20_00L, 30_00L], redemptions.Select(r => r["valueQepik"].ToInt64()).Order());
        Assert.All(redemptions, r => Assert.Equal(UserId(shop.Staff), r["staffUserId"].AsGuid));
        Assert.Equal(2, await GiftRedeemedEventsAsync(host));
    }

    [Fact]
    public async Task Items_are_redeemed_per_line_up_to_the_remaining_quantity()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var shop = await GiftTestData.ShopAsync(host, "gitems");
        var sender = await host.LoginAsync("gitems-sender@example.az");
        var (giftId, token) = await GiftTestData.DeliveredAsync(host, sender, GiftTestData.Items(shop, coffees: 2, cakes: 1));
        var codes = await CodesAsync(host, token);

        using (var preview = await PreviewAsync(host, shop, codes.Short))
        {
            var items = (await host.JsonAsync(preview)).GetProperty("gift").GetProperty("items");
            Assert.Equal(2, items.GetArrayLength());
            Assert.Equal(2, items[0].GetProperty("remainingQty").GetInt32());
        }

        Assert.Equal("gift_insufficient_balance", await RedeemErrorAsync(host, shop, codes.Qr, new { lines = new[] { new { catalogItemId = shop.CoffeeId, qty = 3 } } }));
        Assert.Equal("gift_selection_invalid", await RedeemErrorAsync(host, shop, codes.Qr, new { lines = new[] { new { catalogItemId = shop.HiddenId, qty = 1 } } }));
        Assert.Equal("gift_selection_invalid", await RedeemErrorAsync(host, shop, codes.Qr, new { amountQepik = 1_00 }));

        var partial = await RedeemOkAsync(host, shop, codes.Qr, new { lines = new[] { new { catalogItemId = shop.CoffeeId, qty = 1 } } });
        Assert.Equal(3_50, partial.GetProperty("gift").GetProperty("redeemedNowQepik").GetInt64());
        Assert.Equal(3_50 + 5_00, partial.GetProperty("gift").GetProperty("remainingQepik").GetInt64());

        var rest = await RedeemOkAsync(host, shop, codes.Qr, new
        {
            lines = new[] { new { catalogItemId = shop.CoffeeId, qty = 1 }, new { catalogItemId = shop.CakeId, qty = 1 } },
        });
        Assert.Equal("Redeemed", rest.GetProperty("status").GetString());
        var lines = (await RedemptionsAsync(host, giftId)).SelectMany(r => r["lines"].AsBsonArray).Sum(l => l["qty"].ToInt32());
        Assert.Equal(3, lines);
    }

    [Fact]
    public async Task Business_gift_works_only_at_its_business_and_network_voucher_at_accepting_businesses()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var home = await GiftTestData.ShopAsync(host, "gscope-a", acceptsNetwork: true);
        var accepting = await GiftTestData.ShopAsync(host, "gscope-b", acceptsNetwork: true);
        var refusing = await GiftTestData.ShopAsync(host, "gscope-c");
        var sender = await host.LoginAsync("gscope-sender@example.az");

        var (_, businessToken) = await GiftTestData.DeliveredAsync(host, sender, GiftTestData.Voucher(home, 20_00));
        var businessCodes = await CodesAsync(host, businessToken);
        Assert.Equal("code_not_found", await GiftTestData.ErrorCodeAsync(await PreviewAsync(host, accepting, businessCodes.Qr)));
        Assert.Equal("code_not_found", await RedeemErrorAsync(host, accepting, businessCodes.Short, new { amountQepik = 1_00 }));

        var (_, networkToken) = await GiftTestData.DeliveredAsync(host, sender, new
        {
            kind = "Voucher", scope = "Network", amountQepik = 30_00, recipientName = "Dost", theme = "Congrats", senderDisplay = "Named",
        });
        var networkCodes = await CodesAsync(host, networkToken);
        Assert.Equal("network_voucher_not_accepted", await GiftTestData.ErrorCodeAsync(await PreviewAsync(host, refusing, networkCodes.Qr)));
        Assert.Equal("network_voucher_not_accepted", await RedeemErrorAsync(host, refusing, networkCodes.Qr, new { amountQepik = 1_00 }));
        await RedeemOkAsync(host, accepting, networkCodes.Short, new { amountQepik = 10_00 });
        var final = await RedeemOkAsync(host, home, networkCodes.Qr, new { amountQepik = 20_00 });
        Assert.Equal("Redeemed", final.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Parallel_redemptions_never_overdraw_and_losers_get_gift_changed()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var shop = await GiftTestData.ShopAsync(host, "grace");
        var sender = await host.LoginAsync("grace-sender@example.az");
        var (giftId, token) = await GiftTestData.DeliveredAsync(host, sender, GiftTestData.Voucher(shop, 50_00));
        var codes = await CodesAsync(host, token);

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var response = await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{shop.Id}/redeem", Access(shop.Staff), new { code = codes.Qr, amountQepik = 20_00 });
            return response.IsSuccessStatusCode ? null : await GiftTestData.ErrorCodeAsync(response);
        }));

        var succeeded = results.Count(r => r is null);
        Assert.InRange(succeeded, 1, 2);
        Assert.All(results.Where(r => r is not null), r => Assert.Contains(r, new[] { "gift_changed", "gift_insufficient_balance" }));
        var doc = await GiftTestData.GiftDocAsync(host, giftId);
        Assert.Equal(50_00 - (succeeded * 20_00), doc["remainingQepik"].ToInt64());
        Assert.Equal(succeeded, (await RedemptionsAsync(host, giftId)).Count);
        Assert.Equal(succeeded, await GiftRedeemedEventsAsync(host));
    }

    internal static async Task<(string Qr, string Short)> CodesAsync(ApiTestHost host, string token)
    {
        using var page = await host.GetAsync($"/api/v1/public/gifts/{token}", null);
        var codes = (await host.JsonAsync(page)).GetProperty("codes");
        return (codes.GetProperty("qrPayload").GetString()!, codes.GetProperty("shortCode").GetString()!);
    }

    internal static Task<HttpResponseMessage> PreviewAsync(ApiTestHost host, GiftShop shop, string code) =>
        host.SendAsync(HttpMethod.Post, $"/api/v1/business/{shop.Id}/redeem/preview", Access(shop.Staff), new { code });

    internal static async Task<JsonElement> RedeemOkAsync(ApiTestHost host, GiftShop shop, string code, object selection)
    {
        using var response = await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{shop.Id}/redeem", Access(shop.Staff), Merge(code, selection));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return await host.JsonAsync(response);
    }

    internal static async Task<string?> RedeemErrorAsync(ApiTestHost host, GiftShop shop, string code, object selection) =>
        await GiftTestData.ErrorCodeAsync(await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{shop.Id}/redeem", Access(shop.Staff), Merge(code, selection)));

    private static Dictionary<string, object?> Merge(string code, object selection)
    {
        var body = JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(selection))!;
        body["code"] = code;
        return body;
    }

    internal static async Task<List<BsonDocument>> RedemptionsAsync(ApiTestHost host, string giftId) =>
        await host.Database.GetCollection<BsonDocument>("giftRedemptions")
            .Find(new BsonDocument("giftId", new BsonBinaryData(Guid.Parse(giftId), GuidRepresentation.Standard))).ToListAsync(Ct);

    private static async Task<long> GiftRedeemedEventsAsync(ApiTestHost host) =>
        await host.Database.GetCollection<BsonDocument>(Infrastructure.Messaging.Outbox.MongoOutbox.CollectionName)
            .CountDocumentsAsync(new BsonDocument("type", typeof(Domain.Gifts.GiftRedeemed).FullName), cancellationToken: Ct);
}
