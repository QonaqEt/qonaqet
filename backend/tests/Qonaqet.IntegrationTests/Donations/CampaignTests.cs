using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Application.Features.Campaigns.Jobs;
using Qonaqet.Application.Features.Ledger.Jobs;
using Qonaqet.Application.Features.Social.Jobs;
using Qonaqet.Domain.Ledger;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Payments;
using Qonaqet.IntegrationTests.Social;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Donations;

/// <summary>Kampaniyalar (E08-T05): seed şablonları, ianə → raisedQepik, claim-də kampaniya hovuzu üstün, bitmə və qalığın köçməsi.</summary>
[Collection(PaymentsCollection.Name)]
public sealed class CampaignTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Four_draft_templates_are_seeded_once()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var admin = Access(await RolesTests.LoginAsAdminAsync(host, "camp-seed-admin@example.az"));

        using var list = await host.GetAsync("/api/v1/admin/campaigns?status=Draft", admin);
        var slugs = (await host.JsonAsync(list)).EnumerateArray().Select(c => c.GetProperty("slug").GetString()).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(["27-sentyabr-anim-gunu", "8-noyabr-zefer-gunu", "novruz", "ramazan"], slugs);
    }

    [Fact]
    public async Task Campaign_lifecycle_raises_funds_prefers_its_pool_and_rolls_over_the_rest()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var admin = Access(await RolesTests.LoginAsAdminAsync(host, "campaign-lifecycle-admin@example.az"));
        var (shop, itemId, student) = await DonationTestData.SetupAsync(host, "camp", price: 3_00, category: "student");
        var start = host.Clock.UtcNow;
        await PoolClaimTests.FundAsync(host, student, 10_00); // default hovuz

        var campaign = await CreateActiveAsync(host, admin, student, "telebe-kampaniyasi", start.AddHours(-1), start.AddDays(1));
        var campaignId = campaign.GetProperty("id").GetString()!;
        var campaignPool = campaign.GetProperty("poolId").GetGuid();

        var donor = Access(await host.LoginAsync("camp-donor@example.az"));
        var started = await PoolDonationTests.DonateAsync(host, donor,
            new { campaignId, amountQepik = 20_00, donorDisplay = "Named", donorName = "Aysel" }, "/api/v1/donations/campaign");
        await PayAsync(host, started.GetProperty("paymentUrl").GetString()!);
        await DeliverAsync(host, Fulfillment(host));

        using (var after = await host.GetAsync("/api/v1/admin/campaigns?status=Active", admin))
        {
            var active = Assert.Single((await host.JsonAsync(after)).EnumerateArray());
            Assert.Equal(20_00, active.GetProperty("raisedQepik").GetInt64());
            Assert.Equal(20, active.GetProperty("progressPercent").GetInt32()); // məqsəd 100 AZN
        }

        // Claim əvvəl kampaniya hovuzundan və onun adlı ianəçisi ilə.
        var user = await PoolClaimTests.VerifiedAsync(host, "camp-recipient@example.az", student);
        var claim = await PoolClaimTests.ClaimAsync(host, user, shop.BusinessId, itemId);
        Assert.Equal("Aysel sizə ısmarladı 💚", claim.GetProperty("donorDisplayText").GetString());
        await PoolClaimTests.AssertPoolAsync(host, campaignPool, balance: 20_00, reserved: 3_00);

        // Müddət bitir: kampaniya Ended, istifadə olunmamış 17 AZN default hovuza köçür.
        host.Clock.UtcNow = start.AddDays(1).AddMinutes(1);
        Assert.Equal(2, await RunAsync<EndCampaignsJob>(host));
        await PoolClaimTests.AssertPoolAsync(host, campaignPool, balance: 3_00, reserved: 3_00);
        var defaultPool = (await PoolDonationTests.DefaultPoolAsync(host, student))["_id"].AsGuid;
        await PoolClaimTests.AssertPoolAsync(host, defaultPool, balance: 27_00, reserved: 0);

        // Açıq rezerv sonra buraxılır → növbəti keçid qalan 3 AZN-i də köçürür.
        Assert.Equal(1, await RunAsync<ExpireClaimsJob>(host));
        Assert.Equal(1, await RunAsync<EndCampaignsJob>(host));
        Assert.Equal(0, await RunAsync<EndCampaignsJob>(host));
        await PoolClaimTests.AssertPoolAsync(host, campaignPool, balance: 0, reserved: 0);
        await PoolClaimTests.AssertPoolAsync(host, defaultPool, balance: 30_00, reserved: 0);

        await DeliverAsync(host, Ledger());
        Assert.Equal(0, await LedgerTests.BalanceAsync(host, LedgerAccount.Pool(campaignPool)));
        Assert.Equal(30_00, await LedgerTests.BalanceAsync(host, LedgerAccount.Pool(defaultPool)));
        await RunAsync<ReconcileLedgerJob>(host);
        using (var report = await host.GetAsync("/api/v1/admin/ledger/reconciliation", admin))
        {
            Assert.Equal(0, (await host.JsonAsync(report)).GetProperty("mismatches").GetArrayLength());
        }

        using var late = await PostIdempotentAsync(host, "/api/v1/donations/campaign", donor, new { campaignId, amountQepik = 5_00, donorDisplay = "Anonymous" });
        Assert.Equal("campaign_not_active", (await host.JsonAsync(late)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Admin_validation_slug_and_draft_donations()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var admin = Access(await RolesTests.LoginAsAdminAsync(host, "camp-val-admin@example.az"));
        var everyone = await SocialTestData.CategoryAsync(host, "everyone");
        var now = host.Clock.UtcNow;

        using (var taken = await host.SendAsync(HttpMethod.Post, "/api/v1/admin/campaigns", admin, Input(everyone, "novruz", now, now.AddDays(1))))
        {
            Assert.Equal("campaign_slug_taken", (await host.JsonAsync(taken)).GetProperty("code").GetString());
        }

        using (var backwards = await host.SendAsync(HttpMethod.Post, "/api/v1/admin/campaigns", admin, Input(everyone, "geri", now, now.AddDays(-1))))
        {
            Assert.Equal("campaign_invalid", (await host.JsonAsync(backwards)).GetProperty("code").GetString());
        }

        using var draft = await host.SendAsync(HttpMethod.Post, "/api/v1/admin/campaigns", admin, Input(everyone, "qaralama", now, now.AddDays(3)));
        var draftId = (await host.JsonAsync(draft)).GetProperty("id").GetString();
        var donor = Access(await host.LoginAsync("camp-val-donor@example.az"));
        using var notOpen = await PostIdempotentAsync(host, "/api/v1/donations/campaign", donor, new { campaignId = draftId, amountQepik = 5_00, donorDisplay = "Anonymous" });
        Assert.Equal("campaign_not_active", (await host.JsonAsync(notOpen)).GetProperty("code").GetString());
    }

    internal static async Task<JsonElement> CreateActiveAsync(ApiTestHost host, string admin, string categoryId, string slug, DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        using var create = await host.SendAsync(HttpMethod.Post, "/api/v1/admin/campaigns", admin, Input(categoryId, slug, startsAt, endsAt));
        Assert.True(create.StatusCode == HttpStatusCode.Created, await create.Content.ReadAsStringAsync(Ct));
        var id = (await host.JsonAsync(create)).GetProperty("id").GetString();
        using var activate = await host.SendAsync(HttpMethod.Post, $"/api/v1/admin/campaigns/{id}/activate", admin);
        Assert.True(activate.IsSuccessStatusCode, await activate.Content.ReadAsStringAsync(Ct));
        return await host.JsonAsync(activate);
    }

    private static object Input(string categoryId, string slug, DateTimeOffset startsAt, DateTimeOffset endsAt) =>
        new { title = "Kampaniya", slug, description = "Təsvir", categoryId, startsAt, endsAt, goalQepik = 100_00 };

    private static async Task<int> RunAsync<TJob>(ApiTestHost host)
        where TJob : Application.Abstractions.Jobs.IScheduledJob
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await ActivatorUtilities.CreateInstance<TJob>(scope.ServiceProvider).RunAsync(Ct);
    }
}
