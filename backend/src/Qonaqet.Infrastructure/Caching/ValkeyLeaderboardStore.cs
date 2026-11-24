using Microsoft.Extensions.Logging;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Domain.Leaderboard;
using StackExchange.Redis;

namespace Qonaqet.Infrastructure.Caching;

/// <summary>
/// Lövhələr Valkey sorted set-də (üzv = <c>subjectId</c>, bal = xal). Açar müddətlidir (<see cref="TimeToLive"/>): itən və ya köhnələn keş
/// növbəti oxuda Postgres-dən qurulur. Xətalar "miss" kimi qəbul olunur — keş heç vaxt həqiqət mənbəyi deyil.
/// </summary>
public sealed partial class ValkeyLeaderboardStore(ValkeyConnection connection, ILogger<ValkeyLeaderboardStore> logger) : ILeaderboardStore
{
    /// <summary>Yenidənqurma ilə consumer yeniləməsi üst-üstə düşsə belə, köhnəlmə bu müddətlə məhdudlaşır.</summary>
    public static readonly TimeSpan TimeToLive = TimeSpan.FromMinutes(10);

    /// <summary>Yoxlama və yazı atomikdir: arada müddəti bitən açar TTL-siz, natamam lövhə kimi yenidən yaranmır.</summary>
    private const string SetIfExistsScript = """
        if redis.call('EXISTS', KEYS[1]) == 1 then
            return redis.call('ZADD', KEYS[1], ARGV[1], ARGV[2])
        end
        return 0
        """;

    public async Task<LeaderboardSnapshot?> TopAsync(LeaderboardBoard board, int count, Guid? subjectId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(board);
        try
        {
            var db = await connection.DatabaseAsync().ConfigureAwait(false);
            var key = Key(board);
            if (!await db.KeyExistsAsync(key).WaitAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            var top = await db.SortedSetRangeByRankWithScoresAsync(key, 0, count - 1, Order.Descending).WaitAsync(cancellationToken).ConfigureAwait(false);
            int? rank = null;
            int? points = null;
            if (subjectId is { } id)
            {
                var member = id.ToString();
                if (await db.SortedSetRankAsync(key, member, Order.Descending).WaitAsync(cancellationToken).ConfigureAwait(false) is { } zeroBased)
                {
                    rank = (int)zeroBased + 1;
                    points = (int)(await db.SortedSetScoreAsync(key, member).WaitAsync(cancellationToken).ConfigureAwait(false) ?? 0);
                }
            }

            return new LeaderboardSnapshot([.. top.Select(e => new LeaderboardScore(Guid.Parse(e.Element.ToString()), (int)e.Score))], rank, points);
        }
        catch (Exception ex) when (ValkeyCache.IsTransient(ex) || ex is FormatException)
        {
            LogUnavailable(ex, "top");
            return null;
        }
    }

    public async Task ReplaceAsync(LeaderboardBoard board, IReadOnlyList<LeaderboardScore> scores, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(scores);
        if (scores.Count == 0)
        {
            return; // boş lövhə üçün açar yoxdur: oxu hər dəfə Postgres-ə gedir (ucuz sorğu)
        }

        try
        {
            var db = await connection.DatabaseAsync().ConfigureAwait(false);
            var key = Key(board);
            var staging = (RedisKey)(connection.Prefix + board.CacheKey + ":build:" + Guid.NewGuid().ToString("N"));
            var entries = scores.Select(s => new SortedSetEntry(s.SubjectId.ToString(), s.Points)).ToArray();
            await db.SortedSetAddAsync(staging, entries).WaitAsync(cancellationToken).ConfigureAwait(false);
            await db.KeyExpireAsync(staging, TimeToLive).WaitAsync(cancellationToken).ConfigureAwait(false);
            await db.KeyRenameAsync(staging, key).WaitAsync(cancellationToken).ConfigureAwait(false); // atomik əvəzləmə, TTL ilə birlikdə
        }
        catch (Exception ex) when (ValkeyCache.IsTransient(ex))
        {
            LogUnavailable(ex, "replace");
        }
    }

    public async Task SetIfCachedAsync(LeaderboardBoard board, LeaderboardScore score, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(score);
        try
        {
            var db = await connection.DatabaseAsync().ConfigureAwait(false);
            await db.ScriptEvaluateAsync(SetIfExistsScript, [Key(board)], [score.Points, score.SubjectId.ToString()])
                .WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ValkeyCache.IsTransient(ex))
        {
            LogUnavailable(ex, "set");
        }
    }

    private RedisKey Key(LeaderboardBoard board) => connection.Prefix + board.CacheKey;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Valkey lövhə keşi əlçatan deyil ({Operation}); Postgres işlədilir")]
    private partial void LogUnavailable(Exception exception, string operation);
}
