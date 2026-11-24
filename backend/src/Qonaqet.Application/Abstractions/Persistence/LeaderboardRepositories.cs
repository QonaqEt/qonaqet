using Qonaqet.Domain.Leaderboard;

namespace Qonaqet.Application.Abstractions.Persistence;

/// <summary><c>leaderboard_entries</c> (Postgres): lövhələrin həqiqət mənbəyi; Valkey keşi bundan qurulur.</summary>
public interface ILeaderboardRepository
{
    /// <summary>Lövhənin bütün qazananlarının xalları.</summary>
    Task<IReadOnlyList<LeaderboardScore>> ScoresAsync(LeaderboardBoard board, CancellationToken cancellationToken);

    /// <summary>Qazananın verilən dövrlərdəki xalı (qeydi olmayan dövr 0).</summary>
    Task<IReadOnlyDictionary<LeaderboardPeriod, int>> PointsAsync(
        ScoreSubjectType subjectType, Guid subjectId, IReadOnlyCollection<LeaderboardPeriod> periods, CancellationToken cancellationToken);

    /// <summary>Qazananın son xal hadisələri, yenidən köhnəyə.</summary>
    Task<IReadOnlyList<ScoreEvent>> HistoryAsync(ScoreSubjectType subjectType, Guid subjectId, int limit, CancellationToken cancellationToken);

    /// <summary>Bu dövr üçün bu nişan artıq verilibmi (job-un idempotentliyi).</summary>
    Task<bool> BadgesAwardedAsync(ScoreSubjectType subjectType, BadgeType badge, string periodKey, CancellationToken cancellationToken);

    /// <summary>Qazananın nişanları, yenidən köhnəyə.</summary>
    Task<IReadOnlyList<BadgeAward>> BadgesAsync(ScoreSubjectType subjectType, Guid subjectId, CancellationToken cancellationToken);

    /// <summary>Nişanları yazır; unikal açar eyni dövrdə ikinci nişanı rədd edir.</summary>
    Task AddBadgesAsync(IReadOnlyCollection<BadgeAward> awards, CancellationToken cancellationToken);
}

/// <summary>Lövhədə göstərilə bilən qazanan: yalnız ad (və biznes üçün səhifə slug-u).</summary>
public sealed record LeaderboardSubject(Guid Id, string DisplayName, string? Slug);

/// <summary>
/// Lövhə üçün qazananların görünən adları (Mongo). Yalnız göstərilə bilənlər qaytarılır: istifadəçi — silinməmiş, bloklanmamış və
/// <c>leaderboardOptIn</c>; biznes — <c>Active</c>; şirkət — silinməmiş. Qalanları lövhədən gizlənir.
/// </summary>
public interface ILeaderboardSubjects
{
    Task<IReadOnlyDictionary<Guid, LeaderboardSubject>> VisibleAsync(
        ScoreSubjectType subjectType, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}
