using Qonaqet.Application.Abstractions.Jobs;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Social.Common;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Social.Jobs;

/// <summary>
/// Claim müddəti (Worker, dəqiqədə bir): 30 dəqiqə ərzində təhvil verilməyən rezervləri <c>Expired</c> edir və vahidi təklifə qaytarır.
/// Hər claim atomik şərtli keçidlə götürülür (<c>Reserved</c> → <c>Expired</c>), ona görə iki instans eyni anda işləsə də, ya da alıcı
/// eyni anda ləğv etsə də vahid bir dəfə qaytarılır.
/// </summary>
public sealed class ExpireClaimsJob(IClaimRepository claims, ClaimRelease release, IClock clock) : IScheduledJob
{
    public const string Name = "claim-expiry";

    /// <summary>Bir keçiddə ən çox bu qədər claim (qalanları növbəti keçiddə).</summary>
    public const int BatchSize = 200;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var stale = await claims.ListStaleReservedAsync(clock.UtcNow, BatchSize, cancellationToken).ConfigureAwait(false);
        var expired = 0;
        foreach (var claim in stale)
        {
            if (await release.ExpireAsync(claim, cancellationToken).ConfigureAwait(false))
            {
                expired++;
            }
        }

        return expired;
    }
}
