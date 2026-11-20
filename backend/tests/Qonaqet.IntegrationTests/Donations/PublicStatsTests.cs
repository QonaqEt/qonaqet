using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Payments;
using Qonaqet.IntegrationTests.Social;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Donations;

/// <summary>İctimai aqreqatlar (E08-T06): public stats (anonim, 60 s keş), hovuz xülasəsi (yalnız aqreqat), aktiv kampaniyalar.</summary>
[Collection(PaymentsCollection.Name)]
public sealed class PublicStatsTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Public_stats_count_treats_people_and_partners_anonymously_with_a_60_second_cache()
    {
        fixture.SkipIfUnavailable();
        var cache = new RecordingCache();
        await using var host = await fixture.HostAsync(configure: s =>
        {
            s.RemoveAll<ICache>();
            s.AddSingleton<ICache>(cache);
        });
        var (shop, itemId, student) = await DonationTestData.SetupAsync(host, "stats", price: 3_00, category: "student");
        await PoolClaimTests.FundAsync(host, student, 10_00, donorName: "Leyla");
        await RedeemPoolClaimAsync(host, shop, itemId, student, "stats-r1@example.az");

        using (var first = await host.GetAsync("/api/v1/public/stats", null))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            var json = await host.JsonAsync(first);
            Assert.Equal(1, json.GetProperty("treatsToday").GetInt64());
            Assert.Equal(1, json.GetProperty("peopleTreated").GetInt64());
            Assert.Equal(1, json.GetProperty("partnerBusinesses").GetInt64());
        }

        Assert.Equal(TimeSpan.FromSeconds(60), cache.LastTtl);
        await RedeemPoolClaimAsync(host, shop, itemId, student, "stats-r2@example.az");
        using (var cached = await host.GetAsync("/api/v1/public/stats", null))
        {
            Assert.Equal(1, (await host.JsonAsync(cached)).GetProperty("treatsToday").GetInt64()); // keşdən
        }

        cache.Clear();
        using var fresh = await host.GetAsync("/api/v1/public/stats", null);
        Assert.Equal(2, (await host.JsonAsync(fresh)).GetProperty("treatsToday").GetInt64());
    }

    [Fact]
    public async Task Pools_summary_has_aggregates_only_and_active_campaigns_show_progress()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var (shop, itemId, student) = await DonationTestData.SetupAsync(host, "psum", price: 3_00, category: "student");
        await PoolClaimTests.FundAsync(host, student, 10_00, donorName: "Leyla", email: "psum-leyla@example.az");
        await RedeemPoolClaimAsync(host, shop, itemId, student, "psum-r1@example.az");

        using (var summary = await host.GetAsync("/api/v1/pools/summary", null))
        {
            Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
            var raw = await summary.Content.ReadAsStringAsync(Ct);
            Assert.DoesNotContain("Leyla", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("@", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("userId", raw, StringComparison.OrdinalIgnoreCase);
            var row = Assert.Single(DonationTestData.Json(raw).EnumerateArray(), r => r.GetProperty("categoryId").GetString() == student);
            Assert.Equal(7_00, row.GetProperty("availableQepik").GetInt64());
            Assert.Equal(10_00, row.GetProperty("donatedQepik").GetInt64());
            Assert.Equal(1, row.GetProperty("treatsFunded").GetInt64());
        }

        var admin = Access(await RolesTests.LoginAsAdminAsync(host, "psum-camp-admin@example.az"));
        var now = host.Clock.UtcNow;
        var campaign = await CampaignTests.CreateActiveAsync(host, admin, student, "aktiv-kampaniya", now.AddHours(-1), now.AddDays(2));
        var donor = Access(await host.LoginAsync("psum-camp-donor@example.az"));
        var started = await PoolDonationTests.DonateAsync(host, donor,
            new { campaignId = campaign.GetProperty("id").GetString(), amountQepik = 25_00, donorDisplay = "Anonymous" }, "/api/v1/donations/campaign");
        await PayAsync(host, started.GetProperty("paymentUrl").GetString()!);
        await DeliverAsync(host, Fulfillment(host));

        using var active = await host.GetAsync("/api/v1/campaigns/active", null);
        var item = Assert.Single((await host.JsonAsync(active)).EnumerateArray());
        Assert.Equal("aktiv-kampaniya", item.GetProperty("slug").GetString());
        Assert.Equal(25_00, item.GetProperty("raisedQepik").GetInt64());
        Assert.Equal(25, item.GetProperty("progressPercent").GetInt32());
    }

    private static async Task RedeemPoolClaimAsync(ApiTestHost host, Shop shop, string itemId, string category, string email)
    {
        var user = await PoolClaimTests.VerifiedAsync(host, email, category);
        var claim = await PoolClaimTests.ClaimAsync(host, user, shop.BusinessId, itemId);
        using var redeem = await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{shop.BusinessId}/redeem", SocialTestData.Access(shop.Staff),
            new { code = claim.GetProperty("codes").GetProperty("shortCode").GetString() });
        Assert.True(redeem.IsSuccessStatusCode, await redeem.Content.ReadAsStringAsync(Ct));
    }

    /// <summary>Yaddaşda keş (Valkey-siz): TTL-i qeyd edir.</summary>
    private sealed class RecordingCache : ICache
    {
        private readonly ConcurrentDictionary<string, object?> _items = new();

        public TimeSpan? LastTtl { get; private set; }

        public void Clear() => _items.Clear();

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) =>
            Task.FromResult(_items.TryGetValue(key, out var value) ? (T?)value : default);

        public Task SetAsync<T>(string key, T value, TimeSpan timeToLive, CancellationToken cancellationToken)
        {
            _items[key] = value;
            LastTtl = timeToLive;
            return Task.CompletedTask;
        }

        public Task InvalidateAsync(string key, CancellationToken cancellationToken)
        {
            _items.TryRemove(key, out _);
            return Task.CompletedTask;
        }
    }
}
