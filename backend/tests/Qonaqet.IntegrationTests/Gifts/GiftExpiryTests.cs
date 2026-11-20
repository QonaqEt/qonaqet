using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Gifts.Jobs;
using Qonaqet.Domain.Ledger;
using Qonaqet.Domain.Payments;
using Qonaqet.IntegrationTests.Donations;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Payments;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Gifts;

/// <summary>Müddət bitməsi (E09-T09): 60 gün, 7 gün qalmış bir xatırlatma, qalığın refundu və ya hovuza bağışlanması, idempotent job.</summary>
[Collection(PaymentsCollection.Name)]
public sealed class GiftExpiryTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Unused_value_is_refunded_to_the_payer_after_a_single_reminder()
    {
        fixture.SkipIfUnavailable();
        var emails = new RecordingEmailQueue();
        await using var host = await fixture.HostAsync(configure: s => s.AddSingleton<IEmailQueue>(emails));
        var shop = await GiftTestData.ShopAsync(host, "gexpire");
        var sender = await host.LoginAsync("gexpire-sender@example.az");
        var started = await GiftTestData.CreateAsync(host, sender, GiftTestData.Voucher(shop, 50_00, email: "gexpire-ana@example.az"));
        var giftId = started.GetProperty("giftId").GetString()!;
        await GiftTestData.PayAsync(host, started);
        var token = await GiftTestData.TokenAsync(host, sender, giftId);
        await GiftTestData.DeliverNowAsync(host, giftId);
        var codes = await GiftRedemptionTests.CodesAsync(host, token);
        await GiftRedemptionTests.RedeemOkAsync(host, shop, codes.Qr, new { amountQepik = 20_00 });

        var expiresAt = (await GiftTestData.GiftDocAsync(host, giftId))["expiresAt"].ToUniversalTime();
        Assert.Equal(host.Clock.UtcNow.AddDays(60).UtcDateTime, expiresAt, TimeSpan.FromSeconds(1));

        host.Clock.UtcNow = new DateTimeOffset(expiresAt).AddDays(-8);
        Assert.Equal(0, await RunAsync(host));
        host.Clock.UtcNow = new DateTimeOffset(expiresAt).AddDays(-6);
        Assert.Equal(1, await RunAsync(host));
        Assert.Equal(0, await RunAsync(host));
        var reminder = Assert.Single(emails.Of("gift-expiring"));
        Assert.Equal("gexpire-ana@example.az", reminder.To);
        Assert.Contains("30.00 AZN", reminder.TextBody, StringComparison.Ordinal);

        host.Clock.UtcNow = new DateTimeOffset(expiresAt).AddMinutes(1);
        Assert.Equal(1, await RunAsync(host));
        Assert.Equal(0, await RunAsync(host));

        var doc = await GiftTestData.GiftDocAsync(host, giftId);
        Assert.Equal(("Refunded", 30_00L, 0L), (doc["status"].AsString, doc["refundedQepik"].ToInt64(), doc["refundDueQepik"].ToInt64()));
        var payment = await LoadAsync(host, PaymentId.From(Guid.Parse(started.GetProperty("paymentId").GetString()!)));
        Assert.Equal((PaymentStatus.PartiallyRefunded, 30_00L), (payment.Status, payment.RefundedAmount));
        Assert.Null(await CodesOrNullAsync(host, token));
        Assert.Equal("gift_expired", await GiftRedemptionTests.RedeemErrorAsync(host, shop, codes.Qr, new { amountQepik = 1_00 }));

        await DeliverAsync(host, Ledger());
        Assert.Equal(20_00 - 1_40, await LedgerTests.BalanceAsync(host, LedgerAccount.Business(shop.Id)));
        Assert.Equal(1_40 - 30_00, await LedgerTests.BalanceAsync(host, LedgerAccount.Platform));
    }

    [Fact]
    public async Task Unused_value_is_donated_to_the_pool_and_the_ledger_matches_the_pool()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var sender = await host.LoginAsync("gdonexp-sender@example.az");
        var (giftId, _) = await GiftTestData.DeliveredAsync(host, sender, new
        {
            kind = "Voucher", scope = "Network", amountQepik = 10_00, recipientName = "Dost", theme = "Thanks", senderDisplay = "Named",
            expiryAction = "DonateToPool",
        });
        var poolId = (await GiftTestData.GiftDocAsync(host, giftId))["expiryPoolId"].AsGuid;

        host.Clock.UtcNow = host.Clock.UtcNow.AddDays(61);
        Assert.Equal(1, await RunAsync(host));
        Assert.Equal(0, await RunAsync(host));

        var doc = await GiftTestData.GiftDocAsync(host, giftId);
        Assert.Equal(("Expired", 10_00L, 0L), (doc["status"].AsString, doc["donatedQepik"].ToInt64(), doc["refundedQepik"].ToInt64()));
        await PoolClaimTests.AssertPoolAsync(host, poolId, balance: 10_00, reserved: 0);

        Assert.Equal(1, await DeliverAsync(host, Ledger()));
        Assert.Equal(10_00, await LedgerTests.BalanceAsync(host, LedgerAccount.Pool(poolId)));
        Assert.Equal(0, await DeliverAsync(host, Ledger()));
    }

    private static async Task<string?> CodesOrNullAsync(ApiTestHost host, string token)
    {
        using var page = await host.GetAsync($"/api/v1/public/gifts/{token}", null);
        var body = await host.JsonAsync(page);
        Assert.Equal("Refunded", body.GetProperty("status").GetString());
        Assert.False(body.GetProperty("isRedeemable").GetBoolean());
        return body.TryGetProperty("codes", out var codes) && codes.ValueKind == System.Text.Json.JsonValueKind.Object
            ? codes.GetProperty("qrPayload").GetString()
            : null;
    }

    private static async Task<int> RunAsync(ApiTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await ActivatorUtilities.CreateInstance<ExpireGiftsJob>(scope.ServiceProvider).RunAsync(Ct);
    }
}
