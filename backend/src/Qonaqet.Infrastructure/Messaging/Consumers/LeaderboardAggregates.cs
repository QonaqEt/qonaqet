using Microsoft.EntityFrameworkCore;
using Qonaqet.Domain.Leaderboard;
using Qonaqet.Infrastructure.Persistence.Postgres;

namespace Qonaqet.Infrastructure.Messaging.Consumers;

/// <summary>
/// Xal hadisəsini yazır və onu <c>leaderboard_entries</c>-ə (həftə, ay, bütün zamanlar) <c>INSERT ... ON CONFLICT DO UPDATE</c> ilə əlavə edir —
/// consumer-in açıq Postgres tranzaksiyasında, ona görə hadisə və aqreqat ya birlikdə yazılır, ya heç biri. Mənfi (geri alma) hadisə cəmi azaldır.
/// </summary>
public static class LeaderboardAggregates
{
    public static async Task AddAsync(QonaqetDbContext context, ScoreEvent scoreEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(scoreEvent);
        context.Set<ScoreEvent>().Add(scoreEvent);

        var subjectType = scoreEvent.SubjectType.ToString();
        foreach (var period in LeaderboardPeriod.For(scoreEvent.CreatedAt))
        {
            var periodType = period.Type.ToString();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO qonaqet.leaderboard_entries (id, subject_type, subject_id, period_type, period_key, points, treats, value, updated_at)
                VALUES ({Guid.CreateVersion7()}, {subjectType}, {scoreEvent.SubjectId}, {periodType}, {period.Key},
                        {scoreEvent.Points}, {scoreEvent.Treats}, {scoreEvent.Value}, {scoreEvent.CreatedAt})
                ON CONFLICT (subject_type, subject_id, period_type, period_key) DO UPDATE SET
                    points = leaderboard_entries.points + excluded.points,
                    treats = leaderboard_entries.treats + excluded.treats,
                    value = leaderboard_entries.value + excluded.value,
                    updated_at = GREATEST(leaderboard_entries.updated_at, excluded.updated_at)
                """,
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Qazananın hadisə anının dövrləri üzrə cari xalları (commit-dən sonra keşi yeniləmək üçün).</summary>
    public static async Task<IReadOnlyList<(LeaderboardBoard Board, LeaderboardScore Score)>> CurrentAsync(
        QonaqetDbContext context, ScoreSubjectType subjectType, Guid subjectId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var periods = LeaderboardPeriod.For(at);
        var keys = periods.Select(p => p.Key).ToList();
        var rows = await context.Set<LeaderboardEntry>().AsNoTracking()
            .Where(e => e.SubjectType == subjectType && e.SubjectId == subjectId && keys.Contains(e.PeriodKey))
            .Select(e => new { e.PeriodType, e.PeriodKey, e.Points })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. rows
            .Where(r => periods.Contains(new LeaderboardPeriod(r.PeriodType, r.PeriodKey)))
            .Select(r => (new LeaderboardBoard(subjectType, r.PeriodType, r.PeriodKey), new LeaderboardScore(subjectId, r.Points)))];
    }
}
