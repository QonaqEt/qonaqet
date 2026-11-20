using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.PublicSite.Queries.GetPublicStats;

/// <summary>
/// <c>GET public/stats</c> (anonim, 60 saniyə keş): landing-in canlı sayğacları — bu gün (Bakı günü) təhvil verilən qonaqlıqlar, qonaq olunmuş
/// fərqli insanlar, partnyor bizneslər. Yalnız saylar. Hədiyyələr E09-da əlavə olunur.
/// </summary>
[PublicRequest]
public sealed record GetPublicStatsQuery : IQuery<PublicStatsDto>;

public sealed record PublicStatsDto(long TreatsToday, long PeopleTreated, long PartnerBusinesses);

public sealed class GetPublicStatsHandler(ISocialStatsReader stats, ICache cache, IClock clock) : IQueryHandler<GetPublicStatsQuery, PublicStatsDto>
{
    public const string CacheKey = "public:stats:v1";

    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    public async Task<Result<PublicStatsDto>> Handle(GetPublicStatsQuery request, CancellationToken cancellationToken) =>
        await cache.GetOrSetAsync(CacheKey, CacheDuration, async ct =>
        {
            var startOfDay = BakuTime.StartOfDayUtc(BakuTime.ToBakuDate(clock.UtcNow));
            return new PublicStatsDto(
                await stats.CountRedeemedClaimsSinceAsync(startOfDay, ct).ConfigureAwait(false),
                await stats.CountPeopleTreatedAsync(ct).ConfigureAwait(false),
                await stats.CountActiveBusinessesAsync(ct).ConfigureAwait(false));
        }, cancellationToken).ConfigureAwait(false);
}
