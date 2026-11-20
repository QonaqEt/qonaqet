using System.Net;
using System.Text.Json;
using Qonaqet.IntegrationTests.Gifts;
using Qonaqet.IntegrationTests.Payments;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Corporate;

/// <summary>Hədiyyə sifarişi və CSV validasiyası (E10-T02).</summary>
[Collection(PaymentsCollection.Name)]
public sealed class CorporateOrderTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Csv_is_validated_row_by_row_and_the_total_is_price_times_recipients()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync(configure: CorporateTestData.Services);
        var shop = await GiftTestData.ShopAsync(host, "corder");
        var setup = await CorporateTestData.CorporateAsync(host, "corder-co");

        var order = await CorporateTestData.OrderAsync(host, setup.Hr, new
        {
            kind = "Items", scope = "Business", businessId = shop.Id, items = new[] { new { catalogItemId = shop.CoffeeId, qty = 2 } },
            message = "Təşəkkürlər!", amountQepik = 1,
        });
        var orderId = order.GetProperty("id").GetString()!;
        Assert.Equal(("Draft", 7_00L), (order.GetProperty("status").GetString(), order.GetProperty("unitPriceQepik").GetInt64()));

        const string broken = "name,email\nLeyla,leyla@staff.az\n,boş@staff.az\nAnar,not-an-email\nLeyla 2,LEYLA@staff.az\nNicat,nicat@staff.az\n";
        using (var rejected = await CorporateTestData.UploadAsync(host, setup.Hr, orderId, broken))
        {
            var report = await host.JsonAsync(rejected);
            Assert.False(report.GetProperty("accepted").GetBoolean());
            var errors = report.GetProperty("errors").EnumerateArray().Select(e => (e.GetProperty("line").GetInt32(), e.GetProperty("code").GetString())).ToList();
            Assert.Equal([(3, "name_invalid"), (4, "email_invalid"), (5, "email_duplicate")], errors);
        }

        Assert.Equal(0, (await CorporateTestData.OrderDocAsync(host, orderId))["recipientCount"].ToInt32());

        using (var accepted = await CorporateTestData.UploadAsync(host, setup.Hr, orderId, CorporateTestData.Csv(30)))
        {
            var report = await host.JsonAsync(accepted);
            Assert.True(report.GetProperty("accepted").GetBoolean());
            Assert.Equal((30, 30 * 7_00L), (report.GetProperty("recipientCount").GetInt32(), report.GetProperty("totalQepik").GetInt64()));
        }

        var doc = await CorporateTestData.OrderDocAsync(host, orderId);
        Assert.True(doc["recipientsFileId"].IsGuid);
        Assert.Equal(30, doc["recipients"].AsBsonArray.Count);
    }

    [Fact]
    public async Task Csv_over_five_thousand_rows_is_rejected_and_orders_are_private_to_the_corporate()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync(configure: CorporateTestData.Services);
        var setup = await CorporateTestData.CorporateAsync(host, "climit");
        var order = await CorporateTestData.OrderAsync(host, setup.Hr, new { kind = "Voucher", scope = "Network", amountQepik = 10_00 });
        var orderId = order.GetProperty("id").GetString()!;

        using (var tooMany = await CorporateTestData.UploadAsync(host, setup.Hr, orderId, CorporateTestData.Csv(5_001)))
        {
            var report = await host.JsonAsync(tooMany);
            Assert.False(report.GetProperty("accepted").GetBoolean());
            Assert.Contains(report.GetProperty("errors").EnumerateArray(), e => e.GetProperty("code").GetString() == "too_many_rows");
        }

        using (var exactly = await CorporateTestData.UploadAsync(host, setup.Hr, orderId, CorporateTestData.Csv(5_000)))
        {
            Assert.True((await host.JsonAsync(exactly)).GetProperty("accepted").GetBoolean());
        }

        var other = await CorporateTestData.CorporateAsync(host, "climit-other", "Başqa şirkət");
        using var foreign = await host.GetAsync($"/api/v1/corporate/orders/{orderId}", Access(other.Hr));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using var mine = await host.GetAsync("/api/v1/corporate/orders", Access(other.Hr));
        Assert.Equal(0, (await host.JsonAsync(mine)).GetArrayLength());
    }

    internal static JsonElement Element(string json) => JsonDocument.Parse(json).RootElement;
}
