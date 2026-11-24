using Microsoft.EntityFrameworkCore;
using Qonaqet.Domain.Leaderboard;
using Qonaqet.Infrastructure.Persistence.Postgres;

namespace Qonaqet.Infrastructure.Messaging.Consumers;

/// <summary>
/// Refund və ya geri alınan təhvilin xalını düzəldir: mənbənin (<c>source_type</c>, <c>source_id</c>) hər xal hadisəsi üçün mənfi
/// <see cref="ScoreEvent.Reverse"/> hadisəsi yazır. Çağıran consumer-in tranzaksiyasında işləyir və idempotentdir: artıq geri alınmış
/// hadisə ikinci dəfə geri alınmır (unikal açar <c>{SourceType}Reversal</c>). Partner API-nin 15 dəqiqəlik reverse-i (E13) bunu çağırır.
/// </summary>
public static class ScoreReversal
{
    /// <summary>Yazılan düzəliş hadisələrinin sayını qaytarır (dəyişikliklər çağıranın <c>SaveChanges</c>-i ilə saxlanılır).</summary>
    public static async Task<int> ReverseAsync(
        QonaqetDbContext context, string sourceType, Guid sourceId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceType);
        var reversalType = sourceType + ScoreEvent.ReversalSuffix;
        var events = context.Set<ScoreEvent>();
        var originals = await events.Where(x => x.SourceType == sourceType && x.SourceId == sourceId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var reversed = await events
            .Where(x => x.SourceType == reversalType && x.SourceId == sourceId)
            .Select(x => new { x.SubjectType, x.SubjectId })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var written = 0;
        foreach (var original in originals.Where(o => !reversed.Any(r => r.SubjectType == o.SubjectType && r.SubjectId == o.SubjectId)))
        {
            await LeaderboardAggregates.AddAsync(context, ScoreEvent.Reverse(original), cancellationToken).ConfigureAwait(false);
            written++;
        }

        return written;
    }
}
