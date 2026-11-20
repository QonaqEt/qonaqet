using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Corporate.Jobs;
using Qonaqet.Application.Features.Gifts.Common;
using Qonaqet.Domain.Corporate;
using Qonaqet.Domain.Gifts;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Payments;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Corporate;

/// <summary>Fan-out job-u (E10-T04): hər alıcıya bir hədiyyə, 200-lük paketlər, dublikatsız, yarıda qalan yerdən davam.</summary>
[Collection(PaymentsCollection.Name)]
public sealed class CorporateFanOutTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Paid_order_creates_one_gift_per_recipient_in_batches_without_duplicates()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync(configure: CorporateTestData.Services);
        var setup = await CorporateTestData.CorporateAsync(host, "cfan", "Bakcell");
        var orderId = await CorporatePaymentTests.ReadyOrderAsync(host, setup, recipients: 450, expiryAction: "DonateToPool");
        await PayByBankAsync(host, setup, orderId);

        // Yarıda kəsilmiş əvvəlki cəhdi simulyasiya et: ilk paket yazılıb, sifariş irəliləməyib.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var order = (await scope.ServiceProvider.GetRequiredService<ICorporateGiftOrderRepository>()
                .GetAsync(CorporateGiftOrderId.From(Guid.Parse(orderId)), Ct))!;
            var links = scope.ServiceProvider.GetRequiredService<IGiftLinkTokens>();
            var partial = Enumerable.Range(0, 120).Select(i => Gift.CreateForCorporateOrder(
                order.Content(), order.Id.Value, "Bakcell", null, new GiftRecipient(order.Recipients[i].Name, order.Recipients[i].Email), i, order.Message,
                order.DeliverAt, 60, new GiftExpiryPlan(order.ExpiryAction, order.ExpiryPoolId), links.Issue().Secret, host.Clock.UtcNow)).ToList();
            Assert.Equal(120, await scope.ServiceProvider.GetRequiredService<IGiftRepository>().AddCorporateBatchAsync(partial, Ct));
        }

        Assert.Equal(450 - 120, await RunAsync(host));
        Assert.Equal(0, await RunAsync(host));

        var gifts = await CorporateTestData.GiftsOfAsync(host, orderId);
        Assert.Equal(450, gifts.Count);
        Assert.Equal(450, gifts.Select(g => g["recipientEmail"].AsString).Distinct().Count());
        Assert.All(gifts, g =>
        {
            Assert.Equal(("Scheduled", "Bakcell", "Corporate"), (g["status"].AsString, g["senderName"].AsString, g["theme"].AsString));
            Assert.Equal(MongoDB.Bson.BsonNull.Value, g["senderUserId"]);
            Assert.Equal("DonateToPool", g["expiryAction"].AsString);
        });

        var doc = await CorporateTestData.OrderDocAsync(host, orderId);
        Assert.Equal(("Completed", 450), (doc["status"].AsString, doc["sentCount"].ToInt32()));

        // Çatdırılma job-u korporativ hədiyyələri adi qaydada çatdırır.
        await using var jobScope = host.Services.CreateAsyncScope();
        Assert.Equal(200, await ActivatorUtilities.CreateInstance<Application.Features.Gifts.Jobs.DeliverScheduledGiftsJob>(jobScope.ServiceProvider).RunAsync(Ct));
    }

    internal static async Task PayByBankAsync(ApiTestHost host, CorporateSetup setup, string orderId)
    {
        using (await PostIdempotentAsync(host, $"/api/v1/corporate/orders/{orderId}/pay", Access(setup.Hr), new { method = "BankTransfer" }))
        {
        }

        using var confirm = await host.SendAsync(HttpMethod.Post, $"/api/v1/admin/corporate-orders/{orderId}/confirm-bank-transfer", Access(setup.Admin),
            new { bankReference = "REF-" + orderId[..8] });
        Assert.True(confirm.IsSuccessStatusCode, await confirm.Content.ReadAsStringAsync(Ct));
    }

    internal static async Task<int> RunAsync(ApiTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await ActivatorUtilities.CreateInstance<CorporateFanOutJob>(scope.ServiceProvider).RunAsync(Ct);
    }
}
