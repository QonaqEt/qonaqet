using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Infrastructure.Messaging.Consumers;
using Qonaqet.IntegrationTests.Gifts;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Payments;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Corporate;

/// <summary>Korporativ hesabatlar (E10-T05): saylar, istifadə olunmamış dəyər, alıcı üzrə yalnız bəli/xeyr; CSV və PDF; CSR yalnız aqreqatlar.</summary>
[Collection(PaymentsCollection.Name)]
public sealed class CorporateReportTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Order_report_shows_counts_and_only_redeemed_yes_no_per_email()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync(configure: CorporateTestData.Services);
        var shop = await GiftTestData.ShopAsync(host, "creport", acceptsNetwork: true);
        var setup = await CorporateTestData.CorporateAsync(host, "creport-co", "Pasha");
        var orderId = await CorporatePaymentTests.ReadyOrderAsync(host, setup, recipients: 3, expiryAction: "DonateToPool", amount: 20_00);
        await CorporateFanOutTests.PayByBankAsync(host, setup, orderId);
        await CorporateFanOutTests.RunAsync(host);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await ActivatorUtilities.CreateInstance<Application.Features.Gifts.Jobs.DeliverScheduledGiftsJob>(scope.ServiceProvider).RunAsync(Ct);
        }

        // Bir alıcı hədiyyədən qismən istifadə edir (Kofe aldı, amma hesabatda bu görünməməlidir).
        var first = (await CorporateTestData.GiftsOfAsync(host, orderId)).Single(g => g["corporateRecipientIndex"].ToInt32() == 0);
        var codes = await Application(host).QrAsync(first["_id"].AsGuid);
        await GiftRedemptionTests.RedeemOkAsync(host, shop, codes, new { amountQepik = 7_50 });

        using (var report = await host.GetAsync($"/api/v1/corporate/orders/{orderId}/report", Access(setup.Hr)))
        {
            var body = await host.JsonAsync(report);
            Assert.Equal((3, 3, 1), (body.GetProperty("giftsCreated").GetInt32(), body.GetProperty("delivered").GetInt32(), body.GetProperty("redeemed").GetInt32()));
            Assert.Equal((60_00L, 7_50L, 60_00L - 7_50), (body.GetProperty("totalQepik").GetInt64(), body.GetProperty("redeemedValueQepik").GetInt64(), body.GetProperty("unusedValueQepik").GetInt64()));
            var rows = body.GetProperty("recipients").EnumerateArray().ToList();
            Assert.Equal(3, rows.Count);
            Assert.True(rows[0].GetProperty("redeemed").GetBoolean());
            Assert.Equal(["email", "redeemed"], rows[0].EnumerateObject().Select(p => p.Name));
        }

        using (var csv = await host.GetAsync($"/api/v1/corporate/orders/{orderId}/report.csv", Access(setup.Hr)))
        {
            Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
            var text = Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync(Ct));
            Assert.Contains("bəli", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Kafe", text, StringComparison.Ordinal);
            Assert.DoesNotContain("İşçi", text, StringComparison.Ordinal); // adlar da yox, yalnız email
        }

        // PDF: Worker-də yaradılır (consumer), sonra endirilir.
        using var request = await host.SendAsync(HttpMethod.Post, $"/api/v1/corporate/orders/{orderId}/report.pdf", Access(setup.Hr), new { });
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        var reportId = (await host.JsonAsync(request)).GetProperty("id").GetString()!;
        using (var pending = await host.GetAsync($"/api/v1/corporate/reports/{reportId}/file", Access(setup.Hr)))
        {
            Assert.Equal(HttpStatusCode.Conflict, pending.StatusCode);
        }

        Assert.Equal(1, await DeliverAsync(host, new CorporateReportsConsumer(host.Services.GetRequiredService<IServiceScopeFactory>())));
        using (var status = await host.GetAsync($"/api/v1/corporate/reports/{reportId}", Access(setup.Hr)))
        {
            Assert.Equal("Ready", (await host.JsonAsync(status)).GetProperty("status").GetString());
        }

        using var file = await host.GetAsync($"/api/v1/corporate/reports/{reportId}/file", Access(setup.Hr));
        Assert.Equal("application/pdf", file.Content.Headers.ContentType?.MediaType);
        var pdf = Encoding.UTF8.GetString(await file.Content.ReadAsByteArrayAsync(Ct));
        Assert.StartsWith("%PDF", pdf, StringComparison.Ordinal);
        Assert.Contains("bəli", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("Kafe", pdf, StringComparison.Ordinal);

        // Başqa şirkət bu hesabatı görmür.
        var other = await CorporateTestData.CorporateAsync(host, "creport-other", "Başqa");
        using var foreign = await host.GetAsync($"/api/v1/corporate/reports/{reportId}/file", Access(other.Hr));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task Csr_campaign_report_has_aggregates_only()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync(configure: CorporateTestData.Services);
        var setup = await CorporateTestData.CorporateAsync(host, "ccsr", "SOCAR");
        var category = await Social.SocialTestData.CategoryAsync(host, "student");
        using var created = await host.SendAsync(HttpMethod.Post, "/api/v1/admin/campaigns", Access(setup.Admin), new
        {
            title = "Tələbələrə dəstək", slug = "socar-telebe", categoryId = category, startsAt = host.Clock.UtcNow.AddMinutes(-1),
            endsAt = host.Clock.UtcNow.AddDays(30), goalQepik = 1_000_00, corporateId = setup.CorporateId,
        });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync(Ct));
        var campaignId = (await host.JsonAsync(created)).GetProperty("id").GetString()!;

        using (var list = await host.GetAsync("/api/v1/corporate/campaigns", Access(setup.Hr)))
        {
            Assert.Equal(campaignId, Assert.Single((await host.JsonAsync(list)).EnumerateArray()).GetProperty("id").GetString());
        }

        using (var report = await host.GetAsync($"/api/v1/corporate/campaigns/{campaignId}/report", Access(setup.Hr)))
        {
            var body = await host.JsonAsync(report);
            Assert.Equal(1_000_00, body.GetProperty("goalQepik").GetInt64());
            Assert.Equal(0, body.GetProperty("treatsFunded").GetInt64());
            Assert.DoesNotContain(body.EnumerateObject(), p => p.Name.Contains("email", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("name", StringComparison.OrdinalIgnoreCase));
        }

        using (var csv = await host.GetAsync($"/api/v1/corporate/campaigns/{campaignId}/report.csv", Access(setup.Hr)))
        {
            Assert.Contains("Hədəf", Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync(Ct)), StringComparison.Ordinal);
        }

        var other = await CorporateTestData.CorporateAsync(host, "ccsr-other", "Başqa");
        using var foreign = await host.GetAsync($"/api/v1/corporate/campaigns/{campaignId}/report", Access(other.Hr));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    private static Codes Application(ApiTestHost host) => new(host);

    private sealed class Codes(ApiTestHost host)
    {
        public Task<string> QrAsync(Guid giftId) =>
            Task.FromResult(host.Services.GetRequiredService<IRedemptionCodes>().QrPayload(RedemptionCodeKind.Gift, giftId));
    }
}
