using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Application.Features.Ledger.Jobs;
using Qonaqet.Domain.Ledger;
using Qonaqet.Infrastructure.Persistence.Postgres;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Social;
using static Qonaqet.IntegrationTests.Payments.LedgerTests;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Payments;

/// <summary>Payout-lar (admin bank köçürməsi → ledger) və gecəlik hovuz uzlaşdırması (E07-T06).</summary>
[Collection(PaymentsCollection.Name)]
public sealed class PayoutReconciliationTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Payout_debits_the_business_ledger_and_cannot_exceed_the_balance()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var shop = await SocialTestData.ShopAsync(host, "payout");
        var business = Guid.Parse(shop.BusinessId);
        var admin = SocialTestData.Access(shop.Admin);
        Assert.True(await ProcessAsync(host, Message(ClaimRedeemed(business, amount: 10_00, commissionable: true)))); // balans 9.30

        using (var tooMuch = await PayoutAsync(host, admin, business, 9_31))
        {
            Assert.Equal(HttpStatusCode.Conflict, tooMuch.StatusCode);
            Assert.Equal("payout_exceeds_balance", (await host.JsonAsync(tooMuch)).GetProperty("code").GetString());
        }

        const string key = "payout-key-1";
        Guid payoutId;
        using (var payout = await PayoutAsync(host, admin, business, 5_00, key))
        {
            Assert.Equal(HttpStatusCode.Created, payout.StatusCode);
            payoutId = (await host.JsonAsync(payout)).GetProperty("payoutId").GetGuid();
        }

        using (var replay = await PayoutAsync(host, admin, business, 5_00, key))
        {
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal(payoutId, (await host.JsonAsync(replay)).GetProperty("payoutId").GetGuid());
        }

        Assert.Equal(4_30, await BalanceAsync(host, LedgerAccount.Business(business)));

        using (var ledger = await host.GetAsync($"/api/v1/business/{business}/ledger", SocialTestData.Access(shop.Owner)))
        {
            var json = await host.JsonAsync(ledger);
            Assert.Equal(4_30, json.GetProperty("balanceQepik").GetInt64());
            Assert.Contains(json.GetProperty("items").EnumerateArray(), e => e.GetProperty("type").GetString() == "Payout" && e.GetProperty("amountQepik").GetInt64() == -5_00);
        }

        using (var own = await host.GetAsync($"/api/v1/business/{business}/payouts", SocialTestData.Access(shop.Owner)))
        {
            var json = await host.JsonAsync(own);
            Assert.Equal(1, json.GetProperty("total").GetInt64());
            Assert.Equal("BANK-REF-1", json.GetProperty("items")[0].GetProperty("bankReference").GetString());
        }

        using (var staff = await host.GetAsync($"/api/v1/business/{business}/payouts", SocialTestData.Access(shop.Staff)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, staff.StatusCode);
        }

        using (var missing = await PayoutAsync(host, admin, Guid.CreateVersion7(), 100))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        using (var notAdmin = await PayoutAsync(host, SocialTestData.Access(shop.Owner), business, 100))
        {
            Assert.Equal(HttpStatusCode.Forbidden, notAdmin.StatusCode);
        }
    }

    [Fact]
    public async Task Concurrent_payouts_never_overdraw_the_business()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var shop = await SocialTestData.ShopAsync(host, "race-payout");
        var business = Guid.Parse(shop.BusinessId);
        Assert.True(await ProcessAsync(host, Message(ClaimRedeemed(business, amount: 10_00, commissionable: true)))); // 9.30

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => PayoutAsync(host, SocialTestData.Access(shop.Admin), business, 3_00)));
        var created = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        foreach (var response in responses)
        {
            response.Dispose();
        }

        Assert.Equal(3, created);
        Assert.Equal(30, await BalanceAsync(host, LedgerAccount.Business(business)));
    }

    [Fact]
    public async Task Nightly_reconciliation_reports_pool_mismatches()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var admin = Access(await RolesTests.LoginAsAdminAsync(host, "recon-admin@example.az"));

        using (var before = await host.GetAsync("/api/v1/admin/ledger/reconciliation", admin))
        {
            Assert.Equal(JsonNull, (await host.JsonAsync(before)).GetProperty("ranAt").ValueKind);
        }

        // Toxumlanmış 4 kateqoriyanın default hovuzları: hamısı 0, ledger boş → fərq yoxdur.
        Assert.Equal(4, await RunAsync(host));
        using (var clean = await host.GetAsync("/api/v1/admin/ledger/reconciliation", admin))
        {
            var json = await host.JsonAsync(clean);
            Assert.Equal(4, json.GetProperty("accountsChecked").GetInt32());
            Assert.Equal(0, json.GetProperty("mismatches").GetArrayLength());
        }

        var pools = host.Database.GetCollection<BsonDocument>("pools");
        var all = await pools.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(Ct);
        var drifted = all[0]["_id"].AsGuid;
        var matching = all[1]["_id"].AsGuid;
        await pools.UpdateOneAsync(new BsonDocument("_id", new BsonBinaryData(drifted, GuidRepresentation.Standard)),
            new BsonDocument("$set", new BsonDocument { { "balanceQepik", 5_00L }, { "reservedQepik", 1_00L } }), cancellationToken: Ct);
        await pools.UpdateOneAsync(new BsonDocument("_id", new BsonBinaryData(matching, GuidRepresentation.Standard)),
            new BsonDocument("$set", new BsonDocument { { "balanceQepik", 2_00L }, { "reservedQepik", 50L } }), cancellationToken: Ct);
        var orphan = Guid.CreateVersion7();
        await PostAsync(host, LedgerAccount.Pool(drifted), 3_00);
        await PostAsync(host, LedgerAccount.Pool(matching), 1_50);
        await PostAsync(host, LedgerAccount.Pool(orphan), 1_00);

        Assert.Equal(4, await RunAsync(host));

        using var report = await host.GetAsync("/api/v1/admin/ledger/reconciliation", admin);
        var mismatches = (await host.JsonAsync(report)).GetProperty("mismatches").EnumerateArray().ToList();
        Assert.Equal(2, mismatches.Count);
        var drift = Assert.Single(mismatches, m => m.GetProperty("accountId").GetGuid() == drifted);
        Assert.Equal(4_00, drift.GetProperty("operationalBalanceQepik").GetInt64());
        Assert.Equal(3_00, drift.GetProperty("ledgerBalanceQepik").GetInt64());
        Assert.Equal(1_00, drift.GetProperty("differenceQepik").GetInt64());
        var lost = Assert.Single(mismatches, m => m.GetProperty("accountId").GetGuid() == orphan);
        Assert.Equal(-1_00, lost.GetProperty("differenceQepik").GetInt64());
    }

    private const System.Text.Json.JsonValueKind JsonNull = System.Text.Json.JsonValueKind.Null;

    private static async Task<HttpResponseMessage> PayoutAsync(ApiTestHost host, string token, Guid businessId, long amount, string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/admin/payouts", UriKind.Relative))
        {
            Content = JsonContent.Create(new { businessId, amountQepik = amount, bankReference = "BANK-REF-1" }),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return await host.Client.SendAsync(request, Ct);
    }

    private static async Task PostAsync(ApiTestHost host, LedgerAccount account, long amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
        context.Add(LedgerEntry.Post(account, LedgerEntryType.PoolDeposit, amount, "Test", Guid.CreateVersion7(), Guid.CreateVersion7(), "deposit", host.Clock.UtcNow));
        await context.SaveChangesAsync(Ct);
    }

    private static async Task<int> RunAsync(ApiTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await ActivatorUtilities.CreateInstance<ReconcileLedgerJob>(scope.ServiceProvider).RunAsync(Ct);
    }
}
