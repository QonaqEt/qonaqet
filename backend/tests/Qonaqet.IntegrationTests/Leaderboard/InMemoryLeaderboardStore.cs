using System.Collections.Concurrent;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Domain.Leaderboard;

namespace Qonaqet.IntegrationTests.Leaderboard;

/// <summary>
/// Valkey-siz lövhə keşi: sorted set semantikası (xal çoxdan aza, bərabərlikdə üzv sətri böyükdən kiçiyə). <see cref="Unavailable"/> Valkey-in
/// düşməsini təqlid edir (oxu miss, yazı səssizcə buraxılır).
/// </summary>
public sealed class InMemoryLeaderboardStore : ILeaderboardStore
{
    private readonly ConcurrentDictionary<string, Dictionary<Guid, int>> _boards = new();

    public bool Unavailable { get; set; }

    public int Replacements { get; private set; }

    public bool IsCached(LeaderboardBoard board) => _boards.ContainsKey(board.CacheKey);

    /// <summary>Keşdəki xalı birbaşa dəyişir (məs. köhnəlmiş və ya pozulmuş keşi təqlid etmək üçün).</summary>
    public void Corrupt(LeaderboardBoard board, Guid subjectId, int points) => _boards[board.CacheKey][subjectId] = points;

    /// <summary>Açarın müddəti bitib.</summary>
    public void Expire(LeaderboardBoard board) => _boards.TryRemove(board.CacheKey, out _);

    public Task<LeaderboardSnapshot?> TopAsync(LeaderboardBoard board, int count, Guid? subjectId, CancellationToken cancellationToken)
    {
        if (Unavailable || !_boards.TryGetValue(board.CacheKey, out var scores))
        {
            return Task.FromResult<LeaderboardSnapshot?>(null);
        }

        var ordered = scores
            .OrderByDescending(s => s.Value).ThenByDescending(s => s.Key.ToString(), StringComparer.Ordinal)
            .Select(s => new LeaderboardScore(s.Key, s.Value)).ToList();
        var index = subjectId is { } id ? ordered.FindIndex(s => s.SubjectId == id) : -1;
        return Task.FromResult<LeaderboardSnapshot?>(new LeaderboardSnapshot(
            [.. ordered.Take(count)], index >= 0 ? index + 1 : null, index >= 0 ? ordered[index].Points : null));
    }

    public Task ReplaceAsync(LeaderboardBoard board, IReadOnlyList<LeaderboardScore> scores, CancellationToken cancellationToken)
    {
        if (!Unavailable && scores.Count > 0)
        {
            _boards[board.CacheKey] = scores.ToDictionary(s => s.SubjectId, s => s.Points);
            Replacements++;
        }

        return Task.CompletedTask;
    }

    public Task SetIfCachedAsync(LeaderboardBoard board, LeaderboardScore score, CancellationToken cancellationToken)
    {
        if (!Unavailable && _boards.TryGetValue(board.CacheKey, out var scores))
        {
            scores[score.SubjectId] = score.Points;
        }

        return Task.CompletedTask;
    }
}
