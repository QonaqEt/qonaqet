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

    /// <summary>
    /// Lövhəni <c>score_events</c>-dən yenidən hesablayır (açıq Postgres tranzaksiyasında): lövhənin sətirləri <c>FOR UPDATE</c> ilə kilidlənir ki,
    /// eyni anda gələn xal itməsin; yalnız fərqli sətirlər dəyişir, hadisəsi olmayan sətir sıfırlanır (silinmir). Əməliyyat (dəyişiklik olmasa da)
    /// eyni tranzaksiyada <c>audit_logs</c>-a yazılır.
    /// </summary>
    Task<LeaderboardRecompute> RecomputeAsync(LeaderboardBoard board, Guid? actorId, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>
/// Admin tərəfindən tənzimlənən xal dəyərləri (<c>score_point_settings</c>, append-only tarixçə). Hər dəyişiklik yeni sətirdir; təhvilə həmin anda
/// qüvvədə olan versiya tətbiq olunur. Admin heç nə təyin etməyibsə <c>Leaderboard:Points</c> konfiqurasiyası işləyir.
/// </summary>
public interface IScorePointSettings
{
    /// <summary><paramref name="at"/> anında qüvvədə olan dəyərlər.</summary>
    Task<ScorePointsVersion> AtAsync(DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>Yeni versiya yazır (audit interceptor-u əməliyyatı audit log-a yazır).</summary>
    Task<ScorePointsVersion> SetAsync(ScorePoints points, Guid? actorId, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>Xal dəyərləri və onları kimin, nə vaxt təyin etdiyi (konfiqurasiya default-u üçün <c>null</c>).</summary>
public sealed record ScorePointsVersion(ScorePoints Points, DateTimeOffset? ChangedAt, Guid? ChangedBy);

/// <summary>Yenidən hesablamanın nəticəsi: hadisəsi olan qazanan sayı və dəyişən sətirlər.</summary>
public sealed record LeaderboardRecompute(int Subjects, int Changed);

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
