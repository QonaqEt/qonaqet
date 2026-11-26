using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Domain.Leaderboard;

namespace Qonaqet.Application.Features.Leaderboard.Common;

/// <summary>
/// Lövhəni oxuyur: əvvəl Valkey sorted set-dən; keş boşdursa (və ya Valkey əlçatmazdırsa) Postgres-dən qurur, keşi yeniləyir və cavabı
/// Postgres məlumatından verir. Ona görə Valkey itsə də, köhnəlsə də nəticə Postgres-dəkinə qayıdır (keşin TTL-i qısadır).
/// Sıralama hər iki yolda eynidir: xal çoxdan aza, bərabərlikdə <c>subjectId</c> (sətir) böyükdən kiçiyə — Valkey <c>ZREVRANGE</c> kimi.
/// </summary>
public sealed class LeaderboardReader(ILeaderboardStore store, ILeaderboardRepository entries)
{
    public async Task<LeaderboardSnapshot> ReadAsync(LeaderboardBoard board, int count, Guid? subjectId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        var cached = await store.TopAsync(board, count, subjectId, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        var scores = Ordered(await entries.ScoresAsync(board, cancellationToken).ConfigureAwait(false));
        await store.ReplaceAsync(board, scores, cancellationToken).ConfigureAwait(false);

        int? rank = null;
        int? points = null;
        if (subjectId is { } id)
        {
            var index = scores.ToList().FindIndex(s => s.SubjectId == id);
            if (index >= 0)
            {
                rank = index + 1;
                points = scores[index].Points;
            }
        }

        return new LeaderboardSnapshot([.. scores.Take(count)], rank, points);
    }

    private static IReadOnlyList<LeaderboardScore> Ordered(IEnumerable<LeaderboardScore> scores) =>
        [.. scores.OrderByDescending(s => s.Points).ThenByDescending(s => s.SubjectId.ToString(), StringComparer.Ordinal)];
}
