using System.Net;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Persistence.Mongo;
using static Qonaqet.IntegrationTests.Businesses.BusinessTestData;

namespace Qonaqet.IntegrationTests.Businesses;

/// <summary>Biznes parametrləri (E04-T04).</summary>
[Collection(MongoCollection.Name)]
public sealed class BusinessSettingsTests(MongoFixture mongo)
{
    [Fact]
    public async Task Owner_changes_settings_partially_and_the_change_is_audited()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var admin = await RolesTests.LoginAsAdminAsync(host, "admin@example.az");
        var (owner, id) = await ActiveBusinessAsync(host, admin, "owner@example.az");

        using (var first = await host.SendAsync(HttpMethod.Patch, $"/api/v1/business/{id}/settings", Access(owner),
            new { acceptsPool = true, acceptsNetworkVouchers = true, voucherMinQepik = 500, voucherMaxQepik = 5000 }))
        {
            var body = await host.JsonAsync(first);
            Assert.True(body.GetProperty("acceptsPool").GetBoolean());
            Assert.Equal(5000, body.GetProperty("voucherMaxQepik").GetInt64());
        }

        using (var second = await host.SendAsync(HttpMethod.Patch, $"/api/v1/business/{id}/settings", Access(owner), new { acceptsPool = false }))
        {
            var body = await host.JsonAsync(second);
            Assert.False(body.GetProperty("acceptsPool").GetBoolean());
            Assert.True(body.GetProperty("acceptsNetworkVouchers").GetBoolean());
            Assert.Equal(500, body.GetProperty("voucherMinQepik").GetInt64());
        }

        var audit = await host.Database.GetCollection<BsonDocument>("outboxMessages")
            .Find(Builders<BsonDocument>.Filter.Regex("payload", new BsonRegularExpression("BusinessChangeSettings"))).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, audit.Count);
        Assert.Contains("acceptsPool.before", audit[1]["payload"].ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("voucherMinQepik.before", audit[1]["payload"].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Min_above_max_and_out_of_range_limits_are_rejected_and_staff_cannot_change_settings()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var admin = await RolesTests.LoginAsAdminAsync(host, "admin@example.az");
        var (owner, id) = await ActiveBusinessAsync(host, admin, "owner@example.az");
        var staff = await host.LoginAsync("barista@example.az");
        using (var add = await host.SendAsync(HttpMethod.Post, $"/api/v1/business/{id}/staff", Access(owner), new { email = "barista@example.az" }))
        {
            add.EnsureSuccessStatusCode();
        }

        using var both = await host.SendAsync(HttpMethod.Patch, $"/api/v1/business/{id}/settings", Access(owner), new { voucherMinQepik = 5000, voucherMaxQepik = 1000 });
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);
        Assert.Contains("voucher_range_invalid", await both.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);

        // Default max 200 AZN: yalnız min göndərilib, amma saxlanmış max ilə toqquşur.
        using var oneSide = await host.SendAsync(HttpMethod.Patch, $"/api/v1/business/{id}/settings", Access(owner), new { voucherMinQepik = 30000 });
        Assert.Equal(HttpStatusCode.BadRequest, oneSide.StatusCode);
        Assert.Contains("voucher_range_invalid", await oneSide.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);

        using var range = await host.SendAsync(HttpMethod.Patch, $"/api/v1/business/{id}/settings", Access(owner), new { voucherMaxQepik = 500_000 });
        Assert.Contains("voucher_limit_invalid", await range.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);

        using var byStaff = await host.SendAsync(HttpMethod.Patch, $"/api/v1/business/{id}/settings", Access(staff), new { acceptsPool = true });
        Assert.Equal(HttpStatusCode.Forbidden, byStaff.StatusCode);
    }
}
