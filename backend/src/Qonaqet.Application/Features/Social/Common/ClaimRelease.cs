using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Domain.Social;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Social.Common;

/// <summary>
/// Rezervin bağlanması və vahidin geri qaytarılması (müddət bitməsi, alıcının ləğvi, bloklama, biznesin silinməsi). Status keçidi
/// tranzaksiyadan kənarda şərtli atomik yazılır (<c>Reserved</c> → hədəf); yalnız keçidi edən çağırış vahidi qaytarır, ona görə
/// iki job instansı, ya da job ilə ləğv eyni anda işləsə də vahid bir dəfə buraxılır (idempotent).
/// Hovuz rezervləri (E08) burada <c>PoolRelease</c> ilə genişlənəcək.
/// </summary>
public sealed class ClaimRelease(IClaimRepository claims, IOfferRepository offers, IClock clock)
{
    /// <summary>Müddəti bitmiş rezervi <c>Expired</c> edir; uğurda <c>true</c>.</summary>
    public async Task<bool> ExpireAsync(Claim claim, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (!claim.IsReserved)
        {
            return false;
        }

        claim.Expire(clock.UtcNow);
        return await CloseAsync(claim, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Rezervi <c>Cancelled</c> edir (<paramref name="reason"/> sabit koddur); uğurda <c>true</c>.</summary>
    public async Task<bool> CancelAsync(Claim claim, string reason, Guid? by, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (!claim.IsReserved)
        {
            return false;
        }

        claim.Cancel(reason, clock.UtcNow, by);
        return await CloseAsync(claim, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> CloseAsync(Claim claim, CancellationToken cancellationToken)
    {
        if (!await claims.TryCloseReservationAsync(claim, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        if (claim.Source == ClaimSource.Offer && claim.OfferId is { } offerId && claim.OfferType is { } type)
        {
            await offers.ReleaseUnitAsync(offerId, type, claim.QuotaDate, CancellationToken.None).ConfigureAwait(false);
        }

        return true;
    }
}
