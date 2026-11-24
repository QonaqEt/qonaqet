using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Domain.Leaderboard;
using Qonaqet.Infrastructure.Messaging.Consumers;
using Qonaqet.Infrastructure.Persistence.Postgres;
using Qonaqet.Infrastructure.Persistence.Postgres.Records;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Payments;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Leaderboard;

/// <summary>Admin yenidən hesablaması (E11-T06): <c>score_events</c>-dən düzəliş, dəyişiklik yoxdursa heç nə, audit.</summary>
[Collection(PaymentsCollection.Name)]
public sealed class RecomputeLeaderboardTests(PaymentsFixture fixture)
{
    private static readonly DateTimeOffset InWeek48 = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Recompute_repairs_drifted_rows_from_score_events_and_is_audited()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var admin = await RolesTests.LoginAsAdminAsync(host, "lb-recompute-admin@example.az");
        var (good, drifted, orphan) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        await ScoreAsync(host, good, 4, InWeek48);
        await ScoreAsync(host, drifted, 3, InWeek48);
        await ScoreAsync(host, drifted, 2, InWeek48.AddDays(-7)); // başqa həftə: W48-ə düşmür

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE qonaqet.leaderboard_entries SET points = 999 WHERE subject_id = {drifted} AND period_key = {"2026-W48"}", Ct);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO qonaqet.leaderboard_entries (id, subject_type, subject_id, period_type, period_key, points, treats, value, updated_at)
                VALUES ({Guid.CreateVersion7()}, {"User"}, {orphan}, {"Week"}, {"2026-W48"}, {7}, {1}, {0L}, {InWeek48})
                """,
                Ct);
        }

        var body = new { subjectType = "User", periodType = "Week", periodKey = "2026-W48" };
        using (var first = await host.SendAsync(HttpMethod.Post, "/api/v1/admin/leaderboard/recompute", Access(admin), body))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            var result = await host.JsonAsync(first);
            Assert.Equal((2, 2), (result.GetProperty("subjects").GetInt32(), result.GetProperty("changed").GetInt32()));
        }

        Assert.Equal([(orphan, 0), (drifted, 3), (good, 4)], (await WeekAsync(host)).OrderBy(e => e.Points).Select(e => (e.SubjectId, e.Points)));

        using (var again = await host.SendAsync(HttpMethod.Post, "/api/v1/admin/leaderboard/recompute", Access(admin), body))
        {
            Assert.Equal(0, (await host.JsonAsync(again)).GetProperty("changed").GetInt32());
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var audits = await scope.ServiceProvider.GetRequiredService<QonaqetDbContext>().Set<AuditLogRecord>().AsNoTracking()
                .Where(a => a.Action == "Recompute").ToListAsync(Ct);
            Assert.Equal(2, audits.Count);
            Assert.All(audits, a => Assert.Contains("2026-W48", a.Data, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Only_admins_can_recompute_and_the_key_is_validated()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var user = await host.LoginAsync("lb-recompute-user@example.az");
        var admin = await RolesTests.LoginAsAdminAsync(host, "lb-recompute-admin2@example.az");

        using (var forbidden = await host.SendAsync(
            HttpMethod.Post, "/api/v1/admin/leaderboard/recompute", Access(user), new { subjectType = "User", periodType = "AllTime", periodKey = "all" }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        using var invalid = await host.SendAsync(
            HttpMethod.Post, "/api/v1/admin/leaderboard/recompute", Access(admin), new { subjectType = "User", periodType = "Month", periodKey = "2026-W48" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    private static async Task<IReadOnlyList<LeaderboardEntry>> WeekAsync(ApiTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<QonaqetDbContext>().Set<LeaderboardEntry>().AsNoTracking()
            .Where(e => e.PeriodType == LeaderboardPeriodType.Week && e.PeriodKey == "2026-W48").ToListAsync(Ct);
    }

    private static async Task ScoreAsync(ApiTestHost host, Guid subjectId, int points, DateTimeOffset at)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
        await LeaderboardAggregates.AddAsync(
            context, ScoreEvent.Award(ScoreSubjectType.User, subjectId, ScoringPolicy.ClaimSource, Guid.CreateVersion7(), points, 1, 0, at), Ct);
        await context.SaveChangesAsync(Ct);
    }
}
