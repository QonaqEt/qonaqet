using Qonaqet.Domain.Leaderboard;

namespace Qonaqet.Application.Abstractions.Persistence;

/// <summary><c>leaderboard_entries</c> (Postgres): lövhələrin həqiqət mənbəyi; Valkey keşi bundan qurulur.</summary>
public interface ILeaderboardRepository
{
    /// <summary>Lövhənin bütün qazananlarının xalları.</summary>
    Task<IReadOnlyList<LeaderboardScore>> ScoresAsync(LeaderboardBoard board, CancellationToken cancellationToken);
}
