using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Common;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Gifts.Common;
using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Social;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Social.Common;

/// <summary>Geri almanın nəticəsi: tərs yazının (hədiyyədə) və ya claim-in ID-si və yenilənmiş önizləmə.</summary>
public sealed record ReversedRedemption(Guid RedemptionId, Guid ReversalId, RedemptionPreviewDto Redemption);

/// <summary>
/// Təhvilin geri alınması (15 dəqiqə ərzində, yalnız təhvili edən aktor):
/// <list type="bullet">
/// <item><b>Hədiyyə</b>: qalıq bərpa olunur, append-only jurnala mənfi dəyərli tərs yazı düşür (bir təhvil bir dəfə — unikal indeks),
/// <see cref="GiftRedemptionReversed"/>; toqquşmada bir dəfə təkrar cəhd.</item>
/// <item><b>Claim</b>: <c>Redeemed</c> → <c>Reserved</c> (yeni 30 dəqiqə), hovuz claim-ində rezerv və ianə atribusiyası bərpa olunur,
/// <see cref="ClaimRedemptionReversed"/>.</item>
/// </list>
/// Ledger və xal düzəlişlərini consumer-lər hadisədən yazır.
/// </summary>
public sealed class RedemptionReversal(
    IGiftRedemptionRepository giftRedemptions,
    IGiftRepository gifts,
    GiftTransactions giftTransactions,
    IClaimRepository claims,
    IPoolRepository pools,
    IPoolDonationRepository donations,
    RedemptionCodeResolver resolver,
    MongoTransaction transaction,
    IClock clock)
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    /// <param name="actorId">Geri alan aktor; təhvili də o etməlidir (başqasının təhvili "tapılmadı" sayılır).</param>
    /// <param name="canAccessBusiness">Aktorun təhvilin biznesinə girişi.</param>
    public async Task<Result<ReversedRedemption>> ReverseAsync(
        Guid redemptionId, Guid actorId, Func<Guid, bool> canAccessBusiness, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canAccessBusiness);
        var redemption = await giftRedemptions.GetAsync(GiftRedemptionId.From(redemptionId), cancellationToken).ConfigureAwait(false);
        if (redemption is not null)
        {
            return redemption.IsReversal || redemption.StaffUserId != actorId || !canAccessBusiness(redemption.BusinessId.Value)
                ? IntegrationErrors.RedemptionNotFound
                : await ReverseGiftAsync(redemption, actorId, cancellationToken).ConfigureAwait(false);
        }

        var claim = await claims.GetAsync(ClaimId.From(redemptionId), cancellationToken).ConfigureAwait(false);
        if (claim is null || claim.RedeemedByStaffId != actorId || !canAccessBusiness(claim.BusinessId.Value))
        {
            return claim is { Status: ClaimStatus.Reserved } ? IntegrationErrors.AlreadyReversed : IntegrationErrors.RedemptionNotFound;
        }

        return await ReverseClaimAsync(claim, actorId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<ReversedRedemption>> ReverseGiftAsync(GiftRedemption redemption, Guid actorId, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var now = clock.UtcNow;
            if (now - redemption.CreatedAt > Window)
            {
                return IntegrationErrors.ReverseWindowPassed;
            }

            if (await giftRedemptions.IsReversedAsync(redemption.Id, cancellationToken).ConfigureAwait(false))
            {
                return IntegrationErrors.AlreadyReversed;
            }

            var gift = await gifts.GetAsync(redemption.GiftId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Təhvilin hədiyyəsi tapılmadı.");
            var version = gift.Version;
            var reversal = gift.ReverseRedemption(redemption, actorId, now);
            // Paralel geri almada hədiyyənin versiya şərti uduzanı kəsir; təkrar cəhddə tərs yazı artıq görünür.
            if (await giftTransactions.SaveAsync(gift, version, cancellationToken, ct => giftRedemptions.AddAsync(reversal, ct)).ConfigureAwait(false))
            {
                return new ReversedRedemption(redemption.Id.Value, reversal.Id.Value, GiftRedemptionService.Preview(gift, now));
            }

            gift.DequeueDomainEvents();
            await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(20, 60)), cancellationToken).ConfigureAwait(false);
        }

        return GiftErrors.Changed;
    }

    private async Task<Result<ReversedRedemption>> ReverseClaimAsync(Claim claim, Guid actorId, CancellationToken cancellationToken)
    {
        if (claim.Status != ClaimStatus.Redeemed)
        {
            return IntegrationErrors.AlreadyReversed;
        }

        var now = clock.UtcNow;
        if (claim.RedeemedAt is not { } redeemedAt || now - redeemedAt > Window)
        {
            return IntegrationErrors.ReverseWindowPassed;
        }

        return await transaction.RunAsync(
            async ct =>
            {
                claim.ReverseRedemption(actorId, now);
                var restored = await claims.TryRestoreReservedAsync(claim, ct).ConfigureAwait(false);
                if (restored != ClaimRestore.Restored)
                {
                    claim.DequeueDomainEvents();
                    return restored == ClaimRestore.ActiveReservationExists
                        ? (Result<ReversedRedemption>)IntegrationErrors.ReverseConflict
                        : IntegrationErrors.AlreadyReversed;
                }

                if (claim.Source == ClaimSource.Pool && claim.PoolId is { } poolId && claim.AmountQepik is { } amount)
                {
                    await pools.RestoreReservationAsync(poolId, amount, ct).ConfigureAwait(false);
                    await donations.RestoreAsync(poolId, amount, ct).ConfigureAwait(false);
                }

                var preview = await resolver.PreviewAsync(claim, now, ct).ConfigureAwait(false);
                return new ReversedRedemption(claim.Id.Value, claim.Id.Value, preview);
            },
            cancellationToken).ConfigureAwait(false);
    }
}
