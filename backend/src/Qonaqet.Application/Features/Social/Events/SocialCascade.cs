using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Social.Common;

namespace Qonaqet.Application.Features.Social.Events;

/// <summary>
/// Başqa sahələrin hadisələrinə sosial qonaqlığın reaksiyası (Worker consumer-i çağırır). Hamısı idempotentdir: yalnız hələ
/// <c>Reserved</c> olan claim-lər atomik keçidlə ləğv olunur və vahidləri bir dəfə qaytarılır, ona görə hadisənin təkrar çatdırılması
/// heç nəyi ikiqat etmir.
/// </summary>
public sealed class SocialCascade(IClaimRepository claims, ClaimRelease release)
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
}
