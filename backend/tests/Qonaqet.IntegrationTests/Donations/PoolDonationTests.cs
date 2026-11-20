using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Application.Features.Ledger.Jobs;
using Qonaqet.Domain.Ledger;
using Qonaqet.Infrastructure.Messaging.Inbox;
using Qonaqet.Infrastructure.Persistence.Postgres;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Payments;
using Qonaqet.IntegrationTests.Social;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Donations;

/// <summary>Hovuz ianəsi (E08-T02): ödəniş → təsdiq → hovuz balansı + <c>PoolDeposit</c>, idempotent; ianə qeydi göstərim seçimi ilə.</summary>
[Collection(PaymentsCollection.Name)]
public sealed class PoolDonationTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Confirmed_pool_donation_credits_the_pool_and_the_ledger_once()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var category = await SocialTestData.CategoryAsync(host, "student");
        var donor = await host.LoginAsync("pool-donor@example.az");

        var started = await DonateAsync(host, Access(donor), new { categoryId = category, amountQepik = 5_00, donorDisplay = "Named", donorName = "Leyla" });
        Assert.False(started.GetProperty("refundable").GetBoolean());
        Assert.Contains("geri qaytarılmır", started.GetProperty("notice").GetString(), StringComparison.Ordinal);
        await PayAsync(host, started.GetProperty("paymentUrl").GetString()!);

        Assert.Equal(1, await DeliverAsync(host, Fulfillment(host)));
        Assert.Equal(0, await DeliverAsync(host, Fulfillment(host)));

        var pool = await DefaultPoolAsync(host, category);
        Assert.Equal(5_00, pool["balanceQepik"].ToInt64());
        Assert.Equal(5_00, await LedgerTests.BalanceAsync(host, LedgerAccount.Pool(pool["_id"].AsGuid)));

        var donation = await host.Database.GetCollection<BsonDocument>("poolDonations")
            .Find(new BsonDocument("_id", SocialTestData.Bin(started.GetProperty("donationId").GetString()!))).SingleAsync(Ct);
        Assert.Equal("Confirmed", donation["status"].AsString);
        Assert.Equal("Named", donation["donorDisplay"].AsString);
        Assert.Equal("Leyla", donation["donorName"].AsString);
        Assert.Equal(5_00, donation["remainingQepik"].ToInt64());

        using var mine = await host.GetAsync("/api/v1/me/donations/pools", Access(donor));
        var item = Assert.Single((await host.JsonAsync(mine)).EnumerateArray());
        Assert.Equal(5_00, item.GetProperty("amountQepik").GetInt64());
        Assert.Equal(0, item.GetProperty("usedQepik").GetInt64());

        // Ledger və Mongo uzlaşır.
        await using var scope = host.Services.CreateAsyncScope();
        await ActivatorUtilities.CreateInstance<ReconcileLedgerJob>(scope.ServiceProvider).RunAsync(Ct);
        using var report = await host.GetAsync("/api/v1/admin/ledger/reconciliation", Access(await RolesTests.LoginAsAdminAsync(host, "pool-admin@example.az")));
        Assert.Equal(0, (await host.JsonAsync(report)).GetProperty("mismatches").GetArrayLength());
    }

    [Fact]
    public async Task Concurrent_confirmations_all_reach_the_pool_balance()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var category = await SocialTestData.CategoryAsync(host, "everyone");
        var donor = Access(await host.LoginAsync("pool-many@example.az"));
        for (var i = 0; i < 6; i++)
        {
            var started = await DonateAsync(host, donor, new { categoryId = category, amountQepik = 1_00 + i, donorDisplay = "Anonymous" });
            await PayAsync(host, started.GetProperty("paymentUrl").GetString()!);
        }

        // Mesajlar paralel emal olunur (consumer prefetch-i kimi): eyni hovuz sənədi üzərində tranzaksiya toqquşmaları təkrarlanır.
        List<IntegrationMessage> messages;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
            messages = [.. (await context.OutboxMessages.AsNoTracking().Where(m => m.Type.EndsWith("PaymentConfirmed")).ToListAsync(Ct))
                .Select(m => new IntegrationMessage(m.Id, m.Type, m.Payload))];
        }

        await Task.WhenAll(messages.Select(async m =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            await new InboxProcessor(scope.ServiceProvider.GetRequiredService<QonaqetDbContext>()).ProcessAsync(Fulfillment(host), m, host.Clock.UtcNow, Ct);
        }));

        var expected = Enumerable.Range(0, 6).Sum(i => 1_00L + i);
        var pool = await DefaultPoolAsync(host, category);
        Assert.Equal(expected, pool["balanceQepik"].ToInt64());
        Assert.Equal(expected, await LedgerTests.BalanceAsync(host, LedgerAccount.Pool(pool["_id"].AsGuid)));
    }

    [Fact]
    public async Task Unpaid_donation_is_cancelled_and_amounts_are_validated()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var category = await SocialTestData.CategoryAsync(host, "everyone");
        var donor = Access(await host.LoginAsync("pool-unpaid@example.az"));

        var started = await DonateAsync(host, donor, new { categoryId = category, amountQepik = 2_00, donorDisplay = "Anonymous" });
        await PayAsync(host, started.GetProperty("paymentUrl").GetString()!, paid: false);
        Assert.Equal(1, await DeliverAsync(host, Fulfillment(host)));
        var donation = await host.Database.GetCollection<BsonDocument>("poolDonations")
            .Find(new BsonDocument("_id", SocialTestData.Bin(started.GetProperty("donationId").GetString()!))).SingleAsync(Ct);
        Assert.Equal("Cancelled", donation["status"].AsString);
        Assert.Equal(0, (await DefaultPoolAsync(host, category))["balanceQepik"].ToInt64());

        using var tooSmall = await PostIdempotentAsync(host, "/api/v1/donations/pool", donor, new { categoryId = category, amountQepik = 50, donorDisplay = "Anonymous" });
        Assert.Equal(HttpStatusCode.BadRequest, tooSmall.StatusCode);
        Assert.Contains("donation_amount_invalid", await tooSmall.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    internal static async Task<System.Text.Json.JsonElement> DonateAsync(ApiTestHost host, string token, object body, string path = "/api/v1/donations/pool")
    {
        using var response = await PostIdempotentAsync(host, path, token, body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await host.JsonAsync(response);
    }

    internal static Task<BsonDocument> DefaultPoolAsync(ApiTestHost host, string categoryId) =>
        host.Database.GetCollection<BsonDocument>("pools")
            .Find(new BsonDocument { { "categoryId", SocialTestData.Bin(categoryId) }, { "campaignId", BsonNull.Value } }).SingleAsync(Ct);
}
