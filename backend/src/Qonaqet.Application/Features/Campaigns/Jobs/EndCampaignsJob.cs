using Qonaqet.Application.Abstractions.Jobs;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Campaigns.Jobs;

/// <summary>
/// Kampaniyaların bitməsi (Worker, 5 dəqiqədə bir): müddəti keçmiş <c>Active</c> kampaniyalar atomik keçidlə <c>Ended</c> olur, sonra bitmiş
/// kampaniya hovuzunun istifadə olunmamış qalığı (<c>balance − reserved</c>) kateqoriyanın default hovuzuna köçür (Mongo tranzaksiyası +
/// <c>CampaignRolledOver</c> → ledger <c>CampaignRollover</c>). Bitmiş kampaniyanın hələ açıq rezervləri sonra buraxılsa, növbəti keçid o qalığı
/// da köçürür. Hər addım şərtli olduğundan iki instans eyni anda işləsə də pul iki dəfə köçmür.
/// </summary>
public sealed class EndCampaignsJob(ICampaignRepository campaigns, IPoolRepository pools, IClock clock) : IScheduledJob
{
    public const string Name = "campaign-end";

    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    public const int BatchSize = 100;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var processed = 0;
        foreach (var due in await campaigns.ListDueToEndAsync(now, BatchSize, cancellationToken).ConfigureAwait(false))
        {
            if (await campaigns.TryEndAsync(due.Id, now, cancellationToken).ConfigureAwait(false))
            {
                processed++;
            }
        }

        foreach (var ended in await campaigns.ListEndedAsync(BatchSize, cancellationToken).ConfigureAwait(false))
        {
            if (ended.PoolId is not { } campaignPool)
            {
                continue;
            }

            var target = await pools.GetDefaultForCategoryAsync(ended.CategoryId, cancellationToken).ConfigureAwait(false);
            if (target is not null && await pools.RollOverAsync(campaignPool, target.Id, ended.Id, cancellationToken).ConfigureAwait(false) > 0)
            {
                processed++;
            }
        }

        return processed;
    }
}
