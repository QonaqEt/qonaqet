using Qonaqet.Domain.Leaderboard;

namespace Qonaqet.Application.Abstractions.Services;

/// <summary>Keşdən oxunan lövhə: ilk N yer və (istənilibsə) bir qazananın yeri.</summary>
public sealed record LeaderboardSnapshot(IReadOnlyList<LeaderboardScore> Top, int? Rank, int? Points);

/// <summary>
/// Lövhələrin keşi (Valkey sorted set, açar <see cref="LeaderboardBoard.CacheKey"/>). <b>Heç vaxt həqiqət mənbəyi deyil</b>: açar yoxdursa
/// və ya Valkey əlçatmazdırsa oxu <c>null</c> (miss) qaytarır, yazı səssizcə buraxılır; lövhə Postgres-dən (<c>leaderboard_entries</c>) qurulur.
/// </summary>
public interface ILeaderboardStore
{
    /// <summary>İlk <paramref name="count"/> yer, çoxdan aza; <paramref name="subjectId"/> verilibsə onun yeri (1-dən) və xalı.</summary>
    Task<LeaderboardSnapshot?> TopAsync(LeaderboardBoard board, int count, Guid? subjectId, CancellationToken cancellationToken);

    /// <summary>Lövhəni Postgres-dəki xallarla atomik əvəz edir (müddətli açar).</summary>
    Task ReplaceAsync(LeaderboardBoard board, IReadOnlyList<LeaderboardScore> scores, CancellationToken cancellationToken);

    /// <summary>Keşlənmiş lövhədə qazananın xalını mütləq dəyərlə yeniləyir; lövhə keşdə yoxdursa heç nə etmir (növbəti oxu qurur).</summary>
    Task SetIfCachedAsync(LeaderboardBoard board, LeaderboardScore score, CancellationToken cancellationToken);

    /// <summary>Lövhəni keşdən çıxarır (keş açarı biznes qeydi deyil); növbəti oxu Postgres-dən qurur.</summary>
    Task InvalidateAsync(LeaderboardBoard board, CancellationToken cancellationToken);
}
