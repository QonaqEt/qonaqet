using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Businesses.Common;
using Qonaqet.Application.Features.Gifts.Common;
using Qonaqet.Application.Features.Social.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Social;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Social.Commands.RedeemCode;

/// <summary>
/// <c>POST business/{businessId}/redeem</c>: işçi kodu təhvil verir.
/// <list type="bullet">
/// <item><b>Claim</b>: bir Mongo tranzaksiyasında şərtli status keçidi (<c>Reserved</c> → <c>Redeemed</c>) və <see cref="ClaimRedeemed"/>. İdempotent:
/// təkrar cəhd (və ya iki işçinin eyni anda skanı) <c>already_redeemed</c> alır. <c>BusinessDaily</c>/<c>Suspended</c> üçün komissiya yoxdur.</item>
/// <item><b>Hədiyyə</b>: seçilən sətirlər (<paramref name="Lines"/>) və ya məbləğ (<paramref name="AmountQepik"/>, qismən ola bilər) —
/// <see cref="GiftRedemptionService"/>.</item>
/// </list>
/// Əmrin özü tranzaksiyasızdır: hər budaq öz tranzaksiyasını açır (hədiyyə toqquşmada təzə tranzaksiya ilə təkrar cəhd edir).
/// </summary>
[RequiresActiveAccount]
public sealed record RedeemCodeCommand(BusinessId BusinessId, string Code, IReadOnlyList<GiftLine>? Lines = null, long? AmountQepik = null)
    : ICommand<RedemptionPreviewDto>, INonTransactionalCommand
{
    /// <summary>Kod bearer-dir: loga düşməsin.</summary>
    public override string ToString() => nameof(RedeemCodeCommand);
}

public sealed class RedeemCodeValidator : Validator<RedeemCodeCommand>
{
    public RedeemCodeValidator()
    {
        Rule("code", x => !string.IsNullOrWhiteSpace(x.Code) && x.Code.Length <= 200, "code_invalid", "Kod boş ola bilməz.");
        Rule("lines", x => x.Lines is null || x.Lines.Count <= GiftRules.MaxLines, GiftErrors.SelectionInvalid.Code, GiftErrors.SelectionInvalid.Message);
    }
}

public sealed class RedeemCodeHandler(
    BusinessAccess access,
    RedemptionCodeResolver resolver,
    IClaimRepository claims,
    IPoolRepository pools,
    IPoolDonationRepository donations,
    GiftRedemptionService giftRedemptions,
    MongoTransaction transaction,
    ICurrentUser currentUser,
    IClock clock) : ICommandHandler<RedeemCodeCommand, RedemptionPreviewDto>
{
    public async Task<Result<RedemptionPreviewDto>> Handle(RedeemCodeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var grant = await access.RequireAsync(request.BusinessId, BusinessPermission.Operate, cancellationToken).ConfigureAwait(false);
        if (grant.IsFailure)
        {
            return grant.Error;
        }

        if (!grant.Value.Business.IsActive)
        {
            return BusinessErrors.NotActive;
        }

        var resolved = await resolver.ResolveAnyAsync(request.BusinessId, request.Code, cancellationToken).ConfigureAwait(false);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var staffId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        if (resolved.Value.Gift is { } gift)
        {
            return await giftRedemptions.RedeemAsync(gift, grant.Value.Business, request.Lines, request.AmountQepik, staffId, cancellationToken)
                .ConfigureAwait(false);
        }

        var claim = resolved.Value.Claim!;
        return await transaction.RunAsync(ct => RedeemClaimAsync(claim, staffId, ct), cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<RedemptionPreviewDto>> RedeemClaimAsync(Claim claim, Guid staffId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        if (!claim.IsRedeemableAt(now))
        {
            return RedemptionCodeResolver.NotRedeemable(claim, now);
        }

        claim.Redeem(staffId, now);
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

        return await resolver.PreviewAsync(claim, now, cancellationToken).ConfigureAwait(false);
    }
}
