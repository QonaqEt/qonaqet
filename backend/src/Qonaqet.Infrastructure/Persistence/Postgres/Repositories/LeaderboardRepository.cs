using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Leaderboard.Common;
using Qonaqet.Domain.Leaderboard;
using Qonaqet.Infrastructure.Messaging.Outbox;
using Qonaqet.Infrastructure.Persistence.Postgres.Records;

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

    public async Task<LeaderboardRecompute> RecomputeAsync(LeaderboardBoard board, Guid? actorId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(board);
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Yenidən hesablama yalnız açıq Postgres tranzaksiyasında işləyir.");
        }

        if (!LeaderboardPeriod.TryRange(board.PeriodType, board.PeriodKey, out var range))
        {
            throw new ArgumentException($"Dövr açarı düzgün deyil: {board.PeriodKey}", nameof(board));
        }

        var subjectType = board.SubjectType.ToString();
        var periodType = board.PeriodType.ToString();
        var existing = await context.Set<LeaderboardEntry>()
            .FromSqlInterpolated(
                $"""
                SELECT * FROM qonaqet.leaderboard_entries
                WHERE subject_type = {subjectType} AND period_type = {periodType} AND period_key = {board.PeriodKey}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var events = context.Set<ScoreEvent>().AsNoTracking().Where(e => e.SubjectType == board.SubjectType);
        if (range is { } window)
        {
            events = events.Where(e => e.CreatedAt >= window.Start && e.CreatedAt < window.End);
        }

        var totals = await events
            .GroupBy(e => e.SubjectId)
            .Select(g => new { SubjectId = g.Key, Points = g.Sum(e => e.Points), Treats = g.Sum(e => e.Treats), Value = g.Sum(e => e.Value) })
            .ToDictionaryAsync(t => t.SubjectId, cancellationToken).ConfigureAwait(false);

        var changed = 0;
        foreach (var entry in existing)
        {
            var total = totals.GetValueOrDefault(entry.SubjectId);
            if (entry.Correct(total?.Points ?? 0, total?.Treats ?? 0, total?.Value ?? 0, now))
            {
                changed++;
            }
        }

        var period = new LeaderboardPeriod(board.PeriodType, board.PeriodKey);
        foreach (var total in totals.Values.Where(t => existing.TrueForAll(e => e.SubjectId != t.SubjectId)))
        {
            context.Set<LeaderboardEntry>().Add(LeaderboardEntry.Create(board.SubjectType, total.SubjectId, period, total.Points, total.Treats, total.Value, now));
            changed++;
        }

        context.Add(new AuditLogRecord
        {
            Id = Guid.CreateVersion7(),
            ActorId = actorId,
            ActorType = actorId is null ? "System" : "User",
            Action = "Recompute",
            EntityType = nameof(LeaderboardBoard),
            Data = JsonSerializer.Serialize(
                new { board.SubjectType, board.PeriodType, board.PeriodKey, subjects = totals.Count, changed }, OutboxSerializer.JsonOptions),
            CreatedAt = now,
        });
        return new LeaderboardRecompute(totals.Count, changed);
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
