using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Leaderboard.Common;
using Qonaqet.Domain.Leaderboard;

namespace Qonaqet.Infrastructure.Persistence.Postgres.Repositories;

/// <summary><c>leaderboard_entries</c> EF Core üzərində; <c>(subject_type, period_type, period_key, points DESC)</c> indeksindən istifadə edir.</summary>
public sealed class LeaderboardRepository(QonaqetDbContext context) : ILeaderboardRepository
{
    public async Task<IReadOnlyList<LeaderboardScore>> ScoresAsync(LeaderboardBoard board, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(board);
        return await context.Set<LeaderboardEntry>().AsNoTracking()
            .Where(e => e.SubjectType == board.SubjectType && e.PeriodType == board.PeriodType && e.PeriodKey == board.PeriodKey)
            .OrderByDescending(e => e.Points)
            .Select(e => new LeaderboardScore(e.SubjectId, e.Points))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<LeaderboardPeriod, int>> PointsAsync(
        ScoreSubjectType subjectType, Guid subjectId, IReadOnlyCollection<LeaderboardPeriod> periods, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(periods);
        var keys = periods.Select(p => p.Key).ToList();
        var rows = await context.Set<LeaderboardEntry>().AsNoTracking()
            .Where(e => e.SubjectType == subjectType && e.SubjectId == subjectId && keys.Contains(e.PeriodKey))
            .Select(e => new { e.PeriodType, e.PeriodKey, e.Points })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return periods.Distinct().ToDictionary(
            p => p,
            p => rows.FirstOrDefault(r => r.PeriodType == p.Type && r.PeriodKey == p.Key)?.Points ?? 0);
    }

    public async Task<IReadOnlyList<ScoreEvent>> HistoryAsync(ScoreSubjectType subjectType, Guid subjectId, int limit, CancellationToken cancellationToken) =>
        await context.Set<ScoreEvent>().AsNoTracking()
            .Where(e => e.SubjectType == subjectType && e.SubjectId == subjectId)
            .OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)
            .Take(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<bool> BadgesAwardedAsync(ScoreSubjectType subjectType, BadgeType badge, string periodKey, CancellationToken cancellationToken) =>
        context.Set<BadgeAward>().AnyAsync(b => b.SubjectType == subjectType && b.Badge == badge && b.PeriodKey == periodKey, cancellationToken);

    public async Task<IReadOnlyList<BadgeAward>> BadgesAsync(ScoreSubjectType subjectType, Guid subjectId, CancellationToken cancellationToken) =>
        await context.Set<BadgeAward>().AsNoTracking()
            .Where(b => b.SubjectType == subjectType && b.SubjectId == subjectId)
            .OrderByDescending(b => b.AwardedAt).ThenBy(b => b.Badge)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task AddBadgesAsync(IReadOnlyCollection<BadgeAward> awards, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(awards);
        context.Set<BadgeAward>().AddRange(awards);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Reytinq sahəsinin DI qeydiyyatı (keş store-u <c>AddValkey</c>-dədir; adlar Mongo-dan oxunur).</summary>
public static class LeaderboardRegistration
{
    public static IServiceCollection AddLeaderboard(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<ILeaderboardRepository, LeaderboardRepository>();
        services.AddScoped<LeaderboardReader>();
        services.AddScoped<ILeaderboardSubjects, Mongo.Repositories.LeaderboardSubjectsReader>();
        return services;
    }
}
