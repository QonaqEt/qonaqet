using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Gifts.Jobs;
using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Ledger;
using Qonaqet.Domain.Payments;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Payments;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Gifts;

/// <summary>Ləğv və refund (E09-T08): yalnız Scheduled, tam refund + Refund ledger yazısı, idempotentlik, gözləyən refundun təkrarı.</summary>
[Collection(PaymentsCollection.Name)]
public sealed class CancelGiftTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Sender_cancels_a_scheduled_gift_once_with_a_full_refund_and_ledger_entry()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var shop = await GiftTestData.ShopAsync(host, "gcancel");
        var sender = await host.LoginAsync("gcancel-sender@example.az");
        var started = await GiftTestData.CreateAsync(host, sender, new
        {
            kind = "Voucher", scope = "Business", businessId = shop.Id, amountQepik = 25_00, recipientName = "Ana", theme = "Love",
            senderDisplay = "Named", deliverAt = host.Clock.UtcNow.AddDays(3),
        });
        var giftId = started.GetProperty("giftId").GetString()!;
        var paymentId = PaymentId.From(Guid.Parse(started.GetProperty("paymentId").GetString()!));
        await GiftTestData.PayAsync(host, started);

        var stranger = await host.LoginAsync("gcancel-stranger@example.az");
        Assert.Equal("gift_not_found", await GiftTestData.ErrorCodeAsync(await CancelAsync(host, stranger, giftId)));

        using (var cancel = await CancelAsync(host, sender, giftId))
        {
            Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
            var body = await host.JsonAsync(cancel);
            Assert.Equal("Cancelled", body.GetProperty("status").GetString());
            Assert.Equal(25_00, body.GetProperty("refundedQepik").GetInt64());
        }

        // İdempotent: ikinci ləğv eyni nəticə, ikinci refund yoxdur.
        using (var again = await CancelAsync(host, sender, giftId))
        {
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        }

        var payment = await LoadAsync(host, paymentId);
        Assert.Equal((PaymentStatus.Refunded, 25_00L), (payment.Status, payment.RefundedAmount));
        await using (var scope = host.Services.CreateAsyncScope())
        {
            Assert.Single(await scope.ServiceProvider.GetRequiredService<IPaymentRepository>().ListRefundsAsync(paymentId, Ct));
        }

        var doc = await GiftTestData.GiftDocAsync(host, giftId);
        Assert.Equal((0L, 25_00L), (doc["refundDueQepik"].ToInt64(), doc["refundedQepik"].ToInt64()));
        Assert.Equal(MongoDB.Bson.BsonNull.Value, doc["linkTokenSealed"]);

        await DeliverAsync(host, Ledger());
        Assert.Equal(-25_00, await LedgerTests.BalanceAsync(host, LedgerAccount.Platform));

        // Ləğv olunmuş hədiyyə çatdırılmır.
        host.Clock.UtcNow = host.Clock.UtcNow.AddDays(4);
        await using var jobScope = host.Services.CreateAsyncScope();
        Assert.Equal(0, await ActivatorUtilities.CreateInstance<DeliverScheduledGiftsJob>(jobScope.ServiceProvider).RunAsync(Ct));
    }

    [Fact]
    public async Task Delivered_and_unpaid_gifts_cannot_be_cancelled()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var shop = await GiftTestData.ShopAsync(host, "gnocancel");
        var sender = await host.LoginAsync("gnocancel-sender@example.az");
        var (delivered, _) = await GiftTestData.DeliveredAsync(host, sender, GiftTestData.Voucher(shop));
        Assert.Equal("gift_not_cancellable", await GiftTestData.ErrorCodeAsync(await CancelAsync(host, sender, delivered)));

        var unpaid = await GiftTestData.CreateAsync(host, sender, GiftTestData.Voucher(shop));
        Assert.Equal("gift_not_cancellable", await GiftTestData.ErrorCodeAsync(await CancelAsync(host, sender, unpaid.GetProperty("giftId").GetString()!)));
    }

    [Fact]
    public async Task Pending_refund_is_retried_by_the_job_exactly_once()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var shop = await GiftTestData.ShopAsync(host, "gretry");
        var sender = await host.LoginAsync("gretry-sender@example.az");
        var started = await GiftTestData.CreateAsync(host, sender, GiftTestData.Voucher(shop, 15_00));
        var giftId = GiftId.From(Guid.Parse(started.GetProperty("giftId").GetString()!));
        await GiftTestData.PayAsync(host, started);

        // Provayder çağırışından əvvəl kəsilmiş ləğv: hədiyyə Cancelled, refund gözləyir.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gifts = scope.ServiceProvider.GetRequiredService<IGiftRepository>();
            var gift = (await gifts.GetAsync(giftId, Ct))!;
            var version = gift.Version;
            gift.Cancel(host.Clock.UtcNow, gift.SenderUserId);
            Assert.True(await gifts.TryReplaceAsync(gift, version, Ct));
        }

        Assert.Equal(1, await RunRetryAsync(host));
        Assert.Equal(0, await RunRetryAsync(host));
        var payment = await LoadAsync(host, PaymentId.From(Guid.Parse(started.GetProperty("paymentId").GetString()!)));
        Assert.Equal(15_00, payment.RefundedAmount);
        Assert.Equal(15_00, (await GiftTestData.GiftDocAsync(host, giftId.Value.ToString()))["refundedQepik"].ToInt64());
    }

    private static Task<HttpResponseMessage> CancelAsync(ApiTestHost host, System.Text.Json.JsonElement user, string giftId) =>
        host.SendAsync(HttpMethod.Post, $"/api/v1/gifts/{giftId}/cancel", Access(user), new { });

    private static async Task<int> RunRetryAsync(ApiTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await ActivatorUtilities.CreateInstance<RetryGiftRefundsJob>(scope.ServiceProvider).RunAsync(Ct);
    }
}
