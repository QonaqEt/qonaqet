using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Leaderboard.Jobs;
using Qonaqet.Domain.Leaderboard;
using Qonaqet.Infrastructure.Messaging.Consumers;
using Qonaqet.Infrastructure.Persistence.Postgres;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Payments;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Leaderboard;

/// <summary>Səviyyələr, dövr sonu nişanları və <c>GET me/score</c> (E11-T05).</summary>
[Collection(PaymentsCollection.Name)]
public sealed class BadgesAndScoreTests(PaymentsFixture fixture)
{
    /// <summary>W48 (23–29 noyabr) daxilində bir an.</summary>
    private static readonly DateTimeOffset InWeek48 = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Week_top_three_get_a_badge_once_and_hidden_users_are_skipped()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var logins = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var login = await host.LoginAsync($"badge-{i}@example.az");
            logins.Add(UserId(login));
            await ScoreAsync(host, ScoreSubjectType.User, UserId(login), points: 10 + i, InWeek48);
        }

        var ghost = Guid.CreateVersion7(); // hesabı yoxdur (silinib) → görünmür, yer tutmur
        await ScoreAsync(host, ScoreSubjectType.User, ghost, points: 99, InWeek48);

        host.Clock.UtcNow = new DateTimeOffset(2026, 11, 29, 21, 0, 0, TimeSpan.Zero); // Bakı: 30 noyabr 01:00 — W48 bitib
        Assert.Equal(3, await RunJobAsync(host));
        Assert.Equal(0, await RunJobAsync(host)); // idempotent

        await using var scope = host.Services.CreateAsyncScope();
        var badges = await scope.ServiceProvider.GetRequiredService<QonaqetDbContext>().Set<BadgeAward>().AsNoTracking()
            .Where(b => b.Badge == BadgeType.WeekTop3).ToListAsync(Ct);
        Assert.Equal(
            [(logins[4], 1, 14), (logins[3], 2, 13), (logins[2], 3, 12)],
            badges.OrderBy(b => b.Rank).Select(b => (b.SubjectId, b.Rank, b.Points)));
        Assert.All(badges, b => Assert.Equal("2026-W48", b.PeriodKey));
    }

    [Fact]
    public async Task Month_top_ten_is_awarded_only_after_the_baku_month_ends()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var winner = await host.LoginAsync("badge-month@example.az");
        await ScoreAsync(host, ScoreSubjectType.User, UserId(winner), points: 7, InWeek48);

        host.Clock.UtcNow = new DateTimeOffset(2026, 11, 30, 19, 0, 0, TimeSpan.Zero); // Bakı: 30 noyabr 23:00 — ay bitməyib
        await RunJobAsync(host);
        Assert.DoesNotContain(await BadgesAsync(host, UserId(winner)), b => b.Badge == BadgeType.MonthTop10);

        host.Clock.UtcNow = new DateTimeOffset(2026, 11, 30, 21, 0, 0, TimeSpan.Zero); // Bakı: 1 dekabr 01:00
        await RunJobAsync(host);
        var badge = Assert.Single(await BadgesAsync(host, UserId(winner)), b => b.Badge == BadgeType.MonthTop10);
        Assert.Equal(("2026-11", 1), (badge.PeriodKey, badge.Rank));
    }

    [Fact]
    public async Task My_score_returns_level_points_badges_and_history_without_details()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var me = await host.LoginAsync("score-me@example.az");
        await ScoreAsync(host, ScoreSubjectType.User, UserId(me), points: 48, host.Clock.UtcNow.AddDays(-40));
        await ScoreAsync(host, ScoreSubjectType.User, UserId(me), points: 5, host.Clock.UtcNow);

        using var response = await host.GetAsync("/api/v1/me/score", Access(me));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var score = await host.JsonAsync(response);

        Assert.Equal(("Qonaqpərvər", "Süfrə sahibi", 147), (
            score.GetProperty("level").GetProperty("name").GetString(),
            score.GetProperty("nextLevel").GetProperty("name").GetString(),
            score.GetProperty("pointsToNextLevel").GetInt32()));
        Assert.Equal(53, score.GetProperty("points").GetProperty("allTime").GetInt32());
        Assert.Equal(5, score.GetProperty("points").GetProperty("week").GetInt32());
        var history = score.GetProperty("history").EnumerateArray().ToList();
        Assert.Equal([5, 48], history.Select(h => h.GetProperty("points").GetInt32()));
        Assert.All(history, h => Assert.Equal(["kind", "points", "treats", "at"], h.EnumerateObject().Select(p => p.Name)));
    }

    private static async Task<int> RunJobAsync(ApiTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var job = new AwardPeriodBadgesJob(
            scope.ServiceProvider.GetRequiredService<ILeaderboardRepository>(), scope.ServiceProvider.GetRequiredService<ILeaderboardSubjects>(), host.Clock);
        return await job.RunAsync(Ct);
    }

    private static async Task<IReadOnlyList<BadgeAward>> BadgesAsync(ApiTestHost host, Guid userId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ILeaderboardRepository>().BadgesAsync(ScoreSubjectType.User, userId, Ct);
    }

    private static async Task ScoreAsync(ApiTestHost host, ScoreSubjectType subjectType, Guid subjectId, int points, DateTimeOffset at)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
        await LeaderboardAggregates.AddAsync(context, ScoreEvent.Award(subjectType, subjectId, ScoringPolicy.ClaimSource, Guid.CreateVersion7(), points, 1, 0, at), Ct);
        await context.SaveChangesAsync(Ct);
    }
}
