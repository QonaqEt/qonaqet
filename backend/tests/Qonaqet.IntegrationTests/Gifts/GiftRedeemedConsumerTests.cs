using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Domain.Ledger;
using Qonaqet.Infrastructure.Messaging.Consumers;
using Qonaqet.IntegrationTests.Payments;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Gifts;

/// <summary>GiftRedeemed consumer-ləri (E09-T07): ledger (kredit + 7% komissiya, idempotent), göndərənə və alıcıya bildiriş.</summary>
[Collection(PaymentsCollection.Name)]
public sealed class GiftRedeemedConsumerTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Each_redemption_credits_the_redeeming_business_minus_commission_exactly_once()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var home = await GiftTestData.ShopAsync(host, "gledger-a", acceptsNetwork: true);
        var other = await GiftTestData.ShopAsync(host, "gledger-b", acceptsNetwork: true);
        var sender = await host.LoginAsync("gledger-sender@example.az");

        var (_, voucher) = await GiftTestData.DeliveredAsync(host, sender, GiftTestData.Voucher(home, 50_00));
        var voucherCodes = await GiftRedemptionTests.CodesAsync(host, voucher);
        await GiftRedemptionTests.RedeemOkAsync(host, home, voucherCodes.Qr, new { amountQepik = 20_00 });
        await GiftRedemptionTests.RedeemOkAsync(host, home, voucherCodes.Qr, new { amountQepik = 30_00 });

        var (_, network) = await GiftTestData.DeliveredAsync(host, sender, new
        {
            kind = "Voucher", scope = "Network", amountQepik = 10_00, recipientName = "Dost", theme = "Thanks", senderDisplay = "Named",
        });
        await GiftRedemptionTests.RedeemOkAsync(host, other, (await GiftRedemptionTests.CodesAsync(host, network)).Short, new { amountQepik = 10_00 });

        Assert.Equal(3, await DeliverAsync(host, Ledger()));
        Assert.Equal(0, await DeliverAsync(host, Ledger()));

        // 20 → 1.40, 30 → 2.10 komissiya; şəbəkə vauçeri təhvil verən biznesə: 10 → 0.70.
        Assert.Equal(50_00 - 1_40 - 2_10, await LedgerTests.BalanceAsync(host, LedgerAccount.Business(home.Id)));
        Assert.Equal(10_00 - 70, await LedgerTests.BalanceAsync(host, LedgerAccount.Business(other.Id)));
        Assert.Equal(1_40 + 2_10 + 70, await LedgerTests.BalanceAsync(host, LedgerAccount.Platform));
    }

    [Fact]
    public async Task Sender_and_recipient_are_notified_even_when_the_sender_is_anonymous()
    {
        fixture.SkipIfUnavailable();
        var push = new RecordingPushSender();
        var emails = new RecordingEmailQueue();
        await using var host = await fixture.HostAsync(configure: s =>
        {
            s.AddSingleton<IPushSender>(push);
            s.AddSingleton<IEmailQueue>(emails);
        });
        var shop = await GiftTestData.ShopAsync(host, "gnotify");
        var sender = await host.LoginAsync("gnotify-sender@example.az");
        using (await host.SendAsync(HttpMethod.Post, "/api/v1/me/fcm-token", Access(sender), new { token = "sender-device" }))
        {
        }

        // Alıcının hesabı yoxdur, email-i var → email; göndərən anonimdir → yenə push alır.
        var (_, token) = await GiftTestData.DeliveredAsync(host, sender, GiftTestData.Voucher(shop, 30_00, display: "Anonymous", email: "gnotify-ana@example.az"));
        await GiftRedemptionTests.RedeemOkAsync(host, shop, (await GiftRedemptionTests.CodesAsync(host, token)).Qr, new { amountQepik = 12_00 });

        var consumer = new GiftEventsConsumer(host.Services.GetRequiredService<IServiceScopeFactory>());
        Assert.Equal(1, await DeliverAsync(host, consumer));
        Assert.Equal(0, await DeliverAsync(host, consumer));

        Assert.Equal(["Hədiyyə istifadə edildi"], push.TitlesFor("sender-device").Where(t => t.Contains("istifadə", StringComparison.Ordinal)));
        var email = Assert.Single(emails.Of("gift-redeemed"));
        Assert.Equal("gnotify-ana@example.az", email.To);
        Assert.Contains("12.00 AZN", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("18.00 AZN", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("Anonim dost", email.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain(shop.Shop.BusinessId, email.TextBody, StringComparison.Ordinal);
    }
}
