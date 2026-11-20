using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Application.Features.Social.Jobs;
using Qonaqet.Domain.Ledger;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Payments;
using Qonaqet.IntegrationTests.Social;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Donations;

/// <summary>Hovuz claim-i (E08-T03): acceptsPool, poolAccess, atomik rezerv, paralel claim-lər, buraxılma, təhvil, ledger uyğunluğu.</summary>
[Collection(PaymentsCollection.Name)]
public sealed class PoolClaimTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Pool_claim_reserves_releases_and_redeems_keeping_the_ledger_equal_to_balance_minus_reserved()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var (shop, itemId, student) = await DonationTestData.SetupAsync(host, "pclaim", price: 3_00, category: "student");
        var poolId = await FundAsync(host, student, 10_00);
        var recipient = await VerifiedAsync(host, "pclaim-r1@example.az", student);

        var claim = await ClaimAsync(host, recipient, shop.BusinessId, itemId);
        Assert.Equal("Bir xeyirxah insan sizə ısmarladı 💚", claim.GetProperty("donorDisplayText").GetString());
        await AssertPoolAsync(host, poolId, balance: 10_00, reserved: 3_00);

        // Müddət bitir → məbləğ hovuza qayıdır.
        host.Clock.UtcNow = host.Clock.UtcNow.AddMinutes(31);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            Assert.Equal(1, await ActivatorUtilities.CreateInstance<ExpireClaimsJob>(scope.ServiceProvider).RunAsync(Ct));
        }

        await AssertPoolAsync(host, poolId, balance: 10_00, reserved: 0);

        // Yeni claim və təhvil: hovuz balansı azalır, biznes kredit alır.
        var second = await ClaimAsync(host, recipient, shop.BusinessId, itemId);
        using (var redeem = await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{shop.BusinessId}/redeem", SocialTestData.Access(shop.Staff),
            new { code = second.GetProperty("codes").GetProperty("shortCode").GetString() }))
        {
            Assert.True(redeem.IsSuccessStatusCode, await redeem.Content.ReadAsStringAsync(Ct));
        }

        await AssertPoolAsync(host, poolId, balance: 7_00, reserved: 0);
        await DeliverAsync(host, Ledger());
        Assert.Equal(7_00, await LedgerTests.BalanceAsync(host, LedgerAccount.Pool(poolId)));
        Assert.Equal(3_00 - 21, await LedgerTests.BalanceAsync(host, LedgerAccount.Business(Guid.Parse(shop.BusinessId))));
    }

    [Fact]
    public async Task Parallel_pool_claims_never_overdraw_the_pool()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var (shop, itemId, student) = await DonationTestData.SetupAsync(host, "prace", price: 3_00, category: "student");
        var poolId = await FundAsync(host, student, 10_00);
        var users = new List<string>();
        for (var i = 0; i < 8; i++)
        {
            users.Add(Access(await VerifiedAsync(host, $"prace-{i}@example.az", student)));
        }

        var responses = await Task.WhenAll(users.Select(token =>
            PostAsync(host, token, new { businessId = shop.BusinessId, catalogItemId = itemId })));
        var created = responses.Count(r => r.Status == HttpStatusCode.Created);
        Assert.Equal(3, created);
        Assert.All(responses.Where(r => r.Status != HttpStatusCode.Created), r => Assert.Equal("pool_insufficient", r.Code));
        await AssertPoolAsync(host, poolId, balance: 10_00, reserved: 9_00);

        await DeliverAsync(host, Ledger());
        Assert.Equal(10_00 - 9_00, await LedgerTests.BalanceAsync(host, LedgerAccount.Pool(poolId)));
    }

    [Fact]
    public async Task Eligibility_business_and_spend_limit_rules()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var (shop, itemId, student) = await DonationTestData.SetupAsync(host, "prules", price: 3_00, category: "student");
        await FundAsync(host, student, 20_00);

        // "everyone" açıqdır, amma hovuza girişi yoxdur.
        var unverified = Access(await host.LoginAsync("prules-open@example.az"));
        Assert.Equal("category_not_eligible", (await PostAsync(host, unverified, new { businessId = shop.BusinessId, catalogItemId = itemId })).Code);

        var student1 = await VerifiedAsync(host, "prules-s@example.az", student);
        var first = await ClaimAsync(host, student1, shop.BusinessId, itemId);
        using (await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{shop.BusinessId}/redeem", SocialTestData.Access(shop.Staff),
            new { code = first.GetProperty("codes").GetProperty("shortCode").GetString() }))
        {
        }

        // Tələbə kateqoriyası: gündə 5 AZN hovuz xərci → 3 + 3 > 5.
        Assert.Equal("limit_reached", (await PostAsync(host, Access(student1), new { businessId = shop.BusinessId, catalogItemId = itemId })).Code);

        // Hovuz qəbul etməyən biznes.
        using (await host.SendAsync(HttpMethod.Patch, $"/api/v1/business/{shop.BusinessId}/settings", SocialTestData.Access(shop.Owner), new { acceptsPool = false }))
        {
        }

        var student2 = await VerifiedAsync(host, "prules-s2@example.az", student);
        Assert.Equal("pool_not_accepted", (await PostAsync(host, Access(student2), new { businessId = shop.BusinessId, catalogItemId = itemId })).Code);
    }

    internal static async Task<Guid> FundAsync(ApiTestHost host, string categoryId, long amount, string? donorName = null, string? email = null)
    {
        var donor = Access(await host.LoginAsync(email ?? $"funder-{Guid.NewGuid():N}@example.az"));
        var started = await PoolDonationTests.DonateAsync(host, donor, new
        {
            categoryId, amountQepik = amount, donorDisplay = donorName is null ? "Anonymous" : "Named", donorName,
        });
        await PayAsync(host, started.GetProperty("paymentUrl").GetString()!);
        await DeliverAsync(host, Fulfillment(host));
        return (await PoolDonationTests.DefaultPoolAsync(host, categoryId))["_id"].AsGuid;
    }

    internal static async Task<JsonElement> VerifiedAsync(ApiTestHost host, string email, string categoryId)
    {
        await host.LoginAsync(email);
        await SocialTestData.VerifyAsync(host, email, categoryId, host.Clock.UtcNow.AddYears(1));
        return await host.LoginAsync(email);
    }

    internal static async Task<JsonElement> ClaimAsync(ApiTestHost host, JsonElement user, string businessId, string itemId)
    {
        using var response = await host.SendAsync(HttpMethod.Post, "/api/v1/pool-claims", Access(user), new { businessId, catalogItemId = itemId });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await host.JsonAsync(response);
    }

    internal static async Task<(HttpStatusCode Status, string? Code)> PostAsync(ApiTestHost host, string token, object body)
    {
        using var response = await host.SendAsync(HttpMethod.Post, "/api/v1/pool-claims", token, body);
        var text = await response.Content.ReadAsStringAsync(Ct);
        var code = response.IsSuccessStatusCode ? null : JsonDocument.Parse(text).RootElement.GetProperty("code").GetString();
        return (response.StatusCode, code);
    }

    internal static async Task AssertPoolAsync(ApiTestHost host, Guid poolId, long balance, long reserved)
    {
        var pool = await host.Database.GetCollection<BsonDocument>("pools")
            .Find(new BsonDocument("_id", new BsonBinaryData(poolId, GuidRepresentation.Standard))).SingleAsync(Ct);
        Assert.Equal(balance, pool["balanceQepik"].ToInt64());
        Assert.Equal(reserved, pool["reservedQepik"].ToInt64());
    }
}
