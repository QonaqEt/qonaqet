using Qonaqet.Domain.Leaderboard;

namespace Qonaqet.Application.Abstractions.Persistence;

/// <summary><c>leaderboard_entries</c> (Postgres): lövhələrin həqiqət mənbəyi; Valkey keşi bundan qurulur.</summary>
public interface ILeaderboardRepository
{
    /// <summary>Lövhənin bütün qazananlarının xalları.</summary>
    Task<IReadOnlyList<LeaderboardScore>> ScoresAsync(LeaderboardBoard board, CancellationToken cancellationToken);
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
