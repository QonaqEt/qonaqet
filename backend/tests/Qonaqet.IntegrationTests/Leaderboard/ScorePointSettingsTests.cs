using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Qonaqet.Domain.Leaderboard;
using Qonaqet.Infrastructure.Messaging.Consumers;
using Qonaqet.Infrastructure.Persistence.Postgres;
using Qonaqet.Infrastructure.Persistence.Postgres.Records;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Payments;
using static Qonaqet.IntegrationTests.Payments.LedgerTests;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Leaderboard;

/// <summary>
/// Admin tərəfindən tənzimlənən xal dəyərləri (E11-T08): default konfiqurasiyadır, dəyişiklik yalnız sonrakı təhvillərə təsir edir, tarixçə və audit.
/// Versiya uzaq gələcəkdə (2031) yazılır ki, eyni bazanı paylaşan digər xal testlərinə (2026) təsir etməsin.
/// </summary>
[Collection(PaymentsCollection.Name)]
public sealed class ScorePointSettingsTests(PaymentsFixture fixture)
{
    private static readonly DateTimeOffset ChangedAt = new(2031, 1, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Admin_changes_points_for_later_redemptions_only_and_the_change_is_kept_and_audited()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var admin = await RolesTests.LoginAsAdminAsync(host, "lb-points-admin@example.az");

        using (var defaults = await host.GetAsync("/api/v1/admin/leaderboard/points", Access(admin)))
        {
            var body = await host.JsonAsync(defaults);
            Assert.Equal((2, JsonValueKind.Null), (body.GetProperty("businessClaim").GetInt32(), body.GetProperty("changedAt").ValueKind));
        }

        host.Clock.UtcNow = ChangedAt;
        var update = new
        {
            giftItemUnit = 1, giftVoucherStep = 1, giftVoucherStepQepik = 500L, donorClaim = 3, businessClaim = 5, corporateGift = 1, corporateClaim = 2,
        };
        using (var put = await host.SendAsync(HttpMethod.Put, "/api/v1/admin/leaderboard/points", Access(admin), update))
        {
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        }

        using (var current = await host.GetAsync("/api/v1/admin/leaderboard/points", Access(admin)))
        {
            var body = await host.JsonAsync(current);
            Assert.Equal((5, 3), (body.GetProperty("businessClaim").GetInt32(), body.GetProperty("donorClaim").GetInt32()));
            Assert.Equal(ChangedAt, body.GetProperty("changedAt").GetDateTimeOffset());
        }

        var (before, after) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        await ScoreAsync(host, ClaimRedeemed(before, amount: null, commissionable: false) with { At = ChangedAt.AddHours(-1) });
        await ScoreAsync(host, ClaimRedeemed(after, amount: null, commissionable: false) with { At = ChangedAt.AddHours(1) });

        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
        var points = await context.Set<ScoreEvent>().AsNoTracking()
            .Where(e => e.SubjectId == before || e.SubjectId == after)
            .ToDictionaryAsync(e => e.SubjectId, e => e.Points, Ct);
        Assert.Equal((2, 5), (points[before], points[after]));

        var version = Assert.Single(await context.Set<ScorePointSettingRecord>().AsNoTracking().Where(x => x.CreatedAt == ChangedAt).ToListAsync(Ct));
        Assert.NotNull(version.CreatedBy);
        Assert.True(await context.Set<AuditLogRecord>().AsNoTracking()
            .AnyAsync(a => a.EntityType == nameof(ScorePointSettingRecord) && a.EntityId == version.Id && a.Action == "Create", Ct));
    }

    [Fact]
    public async Task Only_admins_can_change_points_and_values_are_validated()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var user = await host.LoginAsync("lb-points-user@example.az");
        var admin = await RolesTests.LoginAsAdminAsync(host, "lb-points-admin2@example.az");
        var valid = new
        {
            giftItemUnit = 1, giftVoucherStep = 1, giftVoucherStepQepik = 500L, donorClaim = 2, businessClaim = 2, corporateGift = 1, corporateClaim = 2,
        };

        using (var forbidden = await host.SendAsync(HttpMethod.Put, "/api/v1/admin/leaderboard/points", Access(user), valid))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        using (var read = await host.GetAsync("/api/v1/admin/leaderboard/points", Access(user)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        }

        using var invalid = await host.SendAsync(
            HttpMethod.Put, "/api/v1/admin/leaderboard/points", Access(admin), valid with { businessClaim = 101, giftVoucherStepQepik = 0L });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    private static async Task ScoreAsync(ApiTestHost host, Domain.Social.ClaimRedeemed redeemed)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
        await new ScoreConsumer(Options.Create(new LeaderboardOptions()), new InMemoryLeaderboardStore()).HandleAsync(Message(redeemed), context, Ct);
        await context.SaveChangesAsync(Ct);
    }
}
