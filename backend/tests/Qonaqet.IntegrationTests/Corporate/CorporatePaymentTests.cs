using System.Net;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Domain.Payments;
using Qonaqet.IntegrationTests.Payments;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Corporate;

/// <summary>Ödəniş: kart və bank köçürməsi (E10-T03).</summary>
[Collection(PaymentsCollection.Name)]
public sealed class CorporatePaymentTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Card_payment_marks_the_order_paid_through_payriff()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync(configure: CorporateTestData.Services);
        var setup = await CorporateTestData.CorporateAsync(host, "ccard");
        var orderId = await ReadyOrderAsync(host, setup, recipients: 3, expiryAction: "RefundToPayer");

        using (var noKey = await host.SendAsync(HttpMethod.Post, $"/api/v1/corporate/orders/{orderId}/pay", Access(setup.Hr), new { method = "Card" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, noKey.StatusCode);
        }

        using var pay = await PostIdempotentAsync(host, $"/api/v1/corporate/orders/{orderId}/pay", Access(setup.Hr), new { method = "Card" });
        Assert.Equal(HttpStatusCode.OK, pay.StatusCode);
        var started = await host.JsonAsync(pay);
        Assert.Equal(3 * 10_00, started.GetProperty("amountQepik").GetInt64());
        var payment = await LoadAsync(host, PaymentId.From(Guid.Parse(started.GetProperty("paymentId").GetString()!)));
        Assert.Equal((PaymentPurpose.CorporateGiftOrder, 30_00L, Guid.Parse(setup.CorporateId)), (payment.Purpose, payment.Amount, payment.CorporateId!.Value));
        Assert.Equal("AwaitingPayment", (await CorporateTestData.OrderDocAsync(host, orderId))["status"].AsString);

        await PayAsync(host, started.GetProperty("paymentUrl").GetString()!);
        await DeliverAsync(host, Fulfillment(host));
        var doc = await CorporateTestData.OrderDocAsync(host, orderId);
        Assert.Equal("Paid", doc["status"].AsString);
        Assert.Equal(0, await DeliverAsync(host, Fulfillment(host))); // təkrar çatdırılma heç nə dəyişmir
    }

    [Fact]
    public async Task Failed_card_payment_returns_the_order_to_draft()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync(configure: CorporateTestData.Services);
        var setup = await CorporateTestData.CorporateAsync(host, "cfail");
        var orderId = await ReadyOrderAsync(host, setup, recipients: 2, expiryAction: "RefundToPayer");
        using var pay = await PostIdempotentAsync(host, $"/api/v1/corporate/orders/{orderId}/pay", Access(setup.Hr), new { method = "Card" });
        await PayAsync(host, (await host.JsonAsync(pay)).GetProperty("paymentUrl").GetString()!, paid: false);
        await DeliverAsync(host, Fulfillment(host));

        var doc = await CorporateTestData.OrderDocAsync(host, orderId);
        Assert.Equal(("Draft", BsonNull.Value), (doc["status"].AsString, doc["paymentId"]));
    }

    [Fact]
    public async Task Bank_transfer_waits_for_the_admin_reference_and_is_audited()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync(configure: CorporateTestData.Services);
        var setup = await CorporateTestData.CorporateAsync(host, "cbank");

        var refundOrder = await ReadyOrderAsync(host, setup, recipients: 2, expiryAction: "RefundToPayer");
        using (var refused = await PostIdempotentAsync(host, $"/api/v1/corporate/orders/{refundOrder}/pay", Access(setup.Hr), new { method = "BankTransfer" }))
        {
            Assert.Equal("corporate_bank_transfer_requires_donation", (await host.JsonAsync(refused)).GetProperty("code").GetString());
        }

        var orderId = await ReadyOrderAsync(host, setup, recipients: 2, expiryAction: "DonateToPool");
        using (var bank = await PostIdempotentAsync(host, $"/api/v1/corporate/orders/{orderId}/pay", Access(setup.Hr), new { method = "BankTransfer" }))
        {
            var body = await host.JsonAsync(bank);
            Assert.StartsWith("QONAQET-", body.GetProperty("bankTransferReference").GetString(), StringComparison.Ordinal);
        }

        Assert.Equal("AwaitingPayment", (await CorporateTestData.OrderDocAsync(host, orderId))["status"].AsString);

        // Yalnız admin təsdiqləyir.
        using (var byHr = await host.SendAsync(HttpMethod.Post, $"/api/v1/admin/corporate-orders/{orderId}/confirm-bank-transfer", Access(setup.Hr), new { bankReference = "X" }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, byHr.StatusCode);
        }

        using (var confirm = await host.SendAsync(HttpMethod.Post, $"/api/v1/admin/corporate-orders/{orderId}/confirm-bank-transfer", Access(setup.Admin),
            new { bankReference = "KAPITAL-2026-0042" }))
        {
            Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
            Assert.Equal("Paid", (await host.JsonAsync(confirm)).GetProperty("status").GetString());
        }

        using (var again = await host.SendAsync(HttpMethod.Post, $"/api/v1/admin/corporate-orders/{orderId}/confirm-bank-transfer", Access(setup.Admin),
            new { bankReference = "KAPITAL-2026-0042" }))
        {
            Assert.Equal("corporate_order_not_awaiting_bank_transfer", (await host.JsonAsync(again)).GetProperty("code").GetString());
        }

        var doc = await CorporateTestData.OrderDocAsync(host, orderId);
        Assert.Equal(("Paid", "KAPITAL-2026-0042"), (doc["status"].AsString, doc["bankReference"].AsString));
        var audit = await host.Database.GetCollection<BsonDocument>(Infrastructure.Messaging.Outbox.MongoOutbox.CollectionName)
            .Find(new BsonDocument("type", typeof(Application.Abstractions.Persistence.AuditRecorded).FullName)).ToListAsync(Ct);
        Assert.Contains(audit, a => a["payload"].AsString.Contains("CorporateOrderConfirmBankTransfer", StringComparison.Ordinal)
            && a["payload"].AsString.Contains(orderId, StringComparison.OrdinalIgnoreCase));
    }

    internal static async Task<string> ReadyOrderAsync(Identity.ApiTestHost host, CorporateSetup setup, int recipients, string expiryAction, long amount = 10_00)
    {
        var order = await CorporateTestData.OrderAsync(host, setup.Hr, new { kind = "Voucher", scope = "Network", amountQepik = amount, expiryAction, message = "Bayramınız mübarək!" });
        var orderId = order.GetProperty("id").GetString()!;
        using var upload = await CorporateTestData.UploadAsync(host, setup.Hr, orderId, CorporateTestData.Csv(recipients, $"{orderId[..8]}.az"));
        Assert.True((await host.JsonAsync(upload)).GetProperty("accepted").GetBoolean());
        return orderId;
    }
}
