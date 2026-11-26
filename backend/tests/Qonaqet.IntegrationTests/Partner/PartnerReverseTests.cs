using System.Net;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Integration;
using Qonaqet.Domain.Ledger;
using Qonaqet.Domain.Social;
using Qonaqet.IntegrationTests.Gifts;
using Qonaqet.IntegrationTests.Payments;
using Qonaqet.SharedKernel;
using static Qonaqet.IntegrationTests.Payments.LedgerTests;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Partner;

/// <summary>
/// Təhvilin geri alınması (E13-T05): 15 dəqiqə ərzində, yalnız öz təhvili, bir dəfə; qalıq bərpa olunur, append-only jurnala tərs yazı düşür,
/// ledger tərs yazıları xalis balansı sıfırlayır.
/// </summary>
[Collection(PaymentsCollection.Name)]
public sealed class PartnerReverseTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Own_redemption_is_reversed_once_within_15_minutes_and_the_balance_is_restored()
    {
        fixture.SkipIfUnavailable();
        await using var api = await fixture.HostAsync();
        await using var partner = new PartnerTestHost(api);
        var shop = await GiftTestData.ShopAsync(api, "prev");
        var sender = await api.LoginAsync("prev-sender@example.az");
        var (giftId, token) = await GiftTestData.DeliveredAsync(api, sender, GiftTestData.Voucher(shop, 50_00, email: "prev-r@example.az"));
        var codes = await GiftRedemptionTests.CodesAsync(api, token);
        var (_, client, secret) = await PartnerTestData.ClientAsync(api, "prev", [PartnerScopes.RedemptionsWrite], businessIds: [shop.Id]);
        var (_, other, otherSecret) = await PartnerTestData.ClientAsync(api, "prev2", [PartnerScopes.RedemptionsWrite], businessIds: [shop.Id]);
        var access = await partner.AccessTokenAsync(client.GetProperty("clientId").GetString()!, secret);
        var otherAccess = await partner.AccessTokenAsync(other.GetProperty("clientId").GetString()!, otherSecret);

        var redemptionId = await RedeemAsync(api, partner, access, shop.Id, codes.Qr, 20_00, "prev-1");

        using (var foreign = await partner.SendAsync(HttpMethod.Post, $"/partner/v1/redemptions/{redemptionId}/reverse", otherAccess, null, "prev-x"))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }

        using (var reverse = await partner.SendAsync(HttpMethod.Post, $"/partner/v1/redemptions/{redemptionId}/reverse", access, null, "prev-r1"))
        {
            var body = await api.JsonAsync(reverse);
            Assert.True(reverse.IsSuccessStatusCode, body.GetRawText());
            Assert.Equal(50_00, body.GetProperty("redemption").GetProperty("gift").GetProperty("remainingQepik").GetInt64());
        }

        using (var again = await partner.SendAsync(HttpMethod.Post, $"/partner/v1/redemptions/{redemptionId}/reverse", access, null, "prev-r2"))
        {
            Assert.Equal("already_reversed", (await api.JsonAsync(again)).GetProperty("code").GetString());
        }

        var journal = await GiftRedemptionTests.RedemptionsAsync(api, giftId);
        Assert.Equal([-20_00L, 20_00L], journal.Select(r => r["valueQepik"].ToInt64()).Order());
        var doc = await GiftTestData.GiftDocAsync(api, giftId);
        Assert.Equal((50_00L, 0L), (doc["remainingQepik"].ToInt64(), doc["redeemedQepik"].ToInt64()));
        Assert.Equal(1, await api.Database.GetCollection<BsonDocument>("outboxMessages")
            .CountDocumentsAsync(Builders<BsonDocument>.Filter.Regex("type", new BsonRegularExpression("GiftRedemptionReversed")), cancellationToken: Ct));

        // 15 dəqiqədən sonra geri alma yoxdur.
        var late = await RedeemAsync(api, partner, access, shop.Id, codes.Qr, 10_00, "prev-2");
        api.Clock.UtcNow = api.Clock.UtcNow.AddMinutes(16);
        using var expired = await partner.SendAsync(HttpMethod.Post, $"/partner/v1/redemptions/{late}/reverse", access, null, "prev-r3");
        Assert.Equal("reverse_window_passed", (await api.JsonAsync(expired)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Reversal_writes_the_negated_net_ledger_entries_exactly_once()
    {
        fixture.SkipIfUnavailable();
        await using var api = await fixture.HostAsync();
        var business = Guid.CreateVersion7();
        var giftId = StronglyTypedId.New<GiftId>();
        var redemption = StronglyTypedId.New<GiftRedemptionId>();
        var at = new DateTimeOffset(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);
        var redeemed = new GiftRedeemed(
            giftId, redemption, BusinessId.From(business), GiftKind.Voucher, GiftScope.Business, 20_00, 0, Guid.CreateVersion7(), null, null, null, true, at);
        var reversed = new GiftRedemptionReversed(
            giftId, redemption, StronglyTypedId.New<GiftRedemptionId>(), BusinessId.From(business), 20_00, null, true, at.AddMinutes(3));

        Assert.True(await ProcessAsync(api, Message(redeemed)));
        Assert.Equal(20_00 - 1_40, await BalanceAsync(api, LedgerAccount.Business(business)));

        Assert.True(await ProcessAsync(api, Message(reversed)));
        Assert.False(await ProcessAsync(api, Message(reversed) with { }));
        Assert.Equal(0, await BalanceAsync(api, LedgerAccount.Business(business)));
    }

    [Fact]
    public async Task Claim_reversal_without_money_writes_no_ledger_entries()
    {
        fixture.SkipIfUnavailable();
        await using var api = await fixture.HostAsync();
        var business = Guid.CreateVersion7();
        var reversed = new ClaimRedemptionReversed(StronglyTypedId.New<ClaimId>(), BusinessId.From(business), null, null, false, DateTimeOffset.UtcNow);

        Assert.True(await ProcessAsync(api, Message(reversed)));
        Assert.Equal(0, await BalanceAsync(api, LedgerAccount.Business(business)));
    }

    private static async Task<string> RedeemAsync(
        Identity.ApiTestHost api, PartnerTestHost partner, string access, Guid businessId, string code, long amount, string key)
    {
        using var response = await partner.SendAsync(HttpMethod.Post, "/partner/v1/redemptions", access, new { businessId, code, amountQepik = amount }, key);
        var body = await api.JsonAsync(response);
        Assert.True(response.IsSuccessStatusCode, body.GetRawText());
        return body.GetProperty("redemptionId").GetString()!;
    }
}
