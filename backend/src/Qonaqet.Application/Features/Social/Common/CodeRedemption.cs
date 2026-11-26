using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Common;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Gifts.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Social;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Social.Common;

/// <summary>
/// Kodun önizlənməsi və təhvili — işçi (Api) və partnyor (Partner API) eyni qaydalardan keçir. Çağıran biznesə girişi əvvəlcədən yoxlayır.
/// <list type="bullet">
/// <item><b>Claim</b>: bir Mongo tranzaksiyasında şərtli status keçidi (<c>Reserved</c> → <c>Redeemed</c>) və <see cref="ClaimRedeemed"/>; təkrar
/// cəhd <c>already_redeemed</c>.</item>
/// <item><b>Hədiyyə</b>: seçilən sətirlər və ya məbləğ (qismən ola bilər) — <see cref="GiftRedemptionService"/>.</item>
/// </list>
/// </summary>
public sealed class CodeRedemption(
    RedemptionCodeResolver resolver,
    IClaimRepository claims,
    IPoolRepository pools,
    IPoolDonationRepository donations,
    GiftRedemptionService giftRedemptions,
    MongoTransaction transaction,
    IClock clock)
{
    /// <summary>Heç nə dəyişmir. Şəbəkə vauçerini qəbul etməyən biznesdə <c>network_voucher_not_accepted</c>.</summary>
    public async Task<Result<RedemptionPreviewDto>> PreviewAsync(Business business, string code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(business);
        var resolved = await resolver.ResolveAnyAsync(business.Id, code, cancellationToken).ConfigureAwait(false);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        if (resolved.Value.Gift is { } gift)
        {
            if (gift.Scope == GiftScope.Network && !business.AcceptsNetworkVouchers)
            {
                return GiftErrors.NetworkNotAccepted;
            }

            return GiftRedemptionService.Preview(gift, clock.UtcNow);
        }

        return await resolver.PreviewAsync(resolved.Value.Claim!, clock.UtcNow, cancellationToken).ConfigureAwait(false);
    }

    /// <summary><paramref name="actorId"/>: təhvil verən işçi və ya inteqrasiya client-i (<c>staffUserId</c> / <c>redeemedByStaffId</c>).</summary>
    public async Task<Result<RedemptionPreviewDto>> RedeemAsync(
        Business business, string code, IReadOnlyList<GiftLine>? lines, long? amountQepik, Guid actorId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(business);
        if (!business.IsActive)
        {
            return BusinessErrors.NotActive;
        }

        var resolved = await resolver.ResolveAnyAsync(business.Id, code, cancellationToken).ConfigureAwait(false);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        if (resolved.Value.Gift is { } gift)
        {
            return await giftRedemptions.RedeemAsync(gift, business, lines, amountQepik, actorId, cancellationToken).ConfigureAwait(false);
        }

        var claim = resolved.Value.Claim!;
        return await transaction.RunAsync(ct => RedeemClaimAsync(claim, actorId, ct), cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<RedemptionPreviewDto>> RedeemClaimAsync(Claim claim, Guid actorId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        if (!claim.IsRedeemableAt(now))
        {
            return RedemptionCodeResolver.NotRedeemable(claim, now);
        }

        claim.Redeem(actorId, now);
        if (!await claims.TryTransitionAsync(claim, ClaimStatus.Reserved, cancellationToken).ConfigureAwait(false))
        {
            // Başqa işçi/cəhd bizi qabaqladı (və ya rezerv elə indi bağlandı): hadisə yazılmır.
            claim.DequeueDomainEvents();
            return SocialErrors.AlreadyRedeemed;
        }

        // Hovuz claim-i: rezerv eyni tranzaksiyada xərclənir (balance və reserved azalır); ledger-də hovuz artıq PoolReserve ilə azalıb.
        if (claim.Source == ClaimSource.Pool && claim.PoolId is { } poolId && claim.AmountQepik is { } amount
            && !await pools.ConsumeReservationAsync(poolId, amount, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Hovuz {poolId.Value} rezervi tapılmadı.");
        }

        if (claim.Source == ClaimSource.Pool && claim.PoolId is { } fundedPool && claim.AmountQepik is { } spent)
        {
            // Proporsional atribusiya: xərclənən məbləğ ianələrdən FIFO ilə çıxılır.
            await donations.ConsumeAsync(fundedPool, spent, cancellationToken).ConfigureAwait(false);
        }

        return (await resolver.PreviewAsync(claim, now, cancellationToken).ConfigureAwait(false)) with { RedemptionId = claim.Id.Value };
    }
}
