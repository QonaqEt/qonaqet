using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Social.Common;
using Qonaqet.Domain.Businesses;

namespace Qonaqet.Application.Features.Social.Events;

/// <summary>
/// Başqa sahələrin hadisələrinə sosial qonaqlığın reaksiyası (Worker consumer-i çağırır). Hamısı idempotentdir: yalnız hələ
/// <c>Reserved</c> olan claim-lər atomik keçidlə ləğv olunur və vahidləri bir dəfə qaytarılır, ona görə hadisənin təkrar çatdırılması
/// heç nəyi ikiqat etmir; kaskad soft delete/bərpa da yalnız uyğun vəziyyətdəki sənədlərə toxunur.
/// </summary>
public sealed class SocialCascade(IClaimRepository claims, IOfferRepository offers, ClaimRelease release)
{
    public const string UserBlockedReason = "user_blocked";

    /// <summary><c>UserBlocked</c>: bloklanmış istifadəçinin aktiv rezervləri ləğv olunur (yeni claim-i isə <c>[RequiresActiveAccount]</c> dayandırır).</summary>
    public async Task<int> OnUserBlockedAsync(Guid userId, CancellationToken cancellationToken)
    {
        var cancelled = 0;
        foreach (var claim in await claims.ListReservedForUserAsync(userId, cancellationToken).ConfigureAwait(false))
        {
            if (await release.CancelAsync(claim, UserBlockedReason, null, cancellationToken).ConfigureAwait(false))
            {
                cancelled++;
            }
        }

        return cancelled;
    }

    /// <summary>
    /// <c>BusinessDeleted</c>: əvvəl biznesin aktiv rezervləri ləğv olunur (vahidlər hələ aktiv təkliflərə qayıdır), sonra təkliflər
    /// <see cref="BusinessCascade.DeleteReason"/> ilə kaskad soft delete olunur — bərpada eyni vəziyyətdə qayıdırlar.
    /// </summary>
    public async Task<int> OnBusinessDeletedAsync(BusinessId businessId, CancellationToken cancellationToken)
    {
        var cancelled = 0;
        foreach (var claim in await claims.ListReservedForBusinessAsync(businessId, cancellationToken).ConfigureAwait(false))
        {
            if (await release.CancelAsync(claim, BusinessCascade.DeleteReason, null, cancellationToken).ConfigureAwait(false))
            {
                cancelled++;
            }
        }

        await offers.CascadeDeleteForBusinessAsync(businessId, null, cancellationToken).ConfigureAwait(false);
        return cancelled;
    }

    /// <summary><c>BusinessRestored</c>: kaskadla silinmiş təkliflər geri qayıdır (ləğv olunmuş rezervlər isə ləğv olunmuş qalır).</summary>
    public Task<long> OnBusinessRestoredAsync(BusinessId businessId, CancellationToken cancellationToken) =>
        offers.CascadeRestoreForBusinessAsync(businessId, null, cancellationToken);
}
