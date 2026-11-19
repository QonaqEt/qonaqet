using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Businesses.Common;
using Qonaqet.Application.Features.Social.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Social;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Social.Commands.RedeemCode;

/// <summary>
/// <c>POST business/{businessId}/redeem</c>: işçi claim-i təhvil verir. Bir Mongo tranzaksiyasında şərtli status keçidi
/// (<c>Reserved</c> → <c>Redeemed</c>) və <see cref="ClaimRedeemed"/> outbox hadisəsi. İdempotent: təkrar cəhd (və ya iki işçinin eyni
/// anda skanı) <c>already_redeemed</c> alır, ikinci hadisə yaranmır. <c>BusinessDaily</c>/<c>Suspended</c> üçün komissiya yoxdur.
/// </summary>
[RequiresActiveAccount]
public sealed record RedeemCodeCommand(BusinessId BusinessId, string Code) : ICommand<RedemptionPreviewDto>;

public sealed class RedeemCodeValidator : Validator<RedeemCodeCommand>
{
    public RedeemCodeValidator()
    {
        Rule("code", x => !string.IsNullOrWhiteSpace(x.Code) && x.Code.Length <= 200, "code_invalid", "Kod boş ola bilməz.");
    }
}

public sealed class RedeemCodeHandler(
    BusinessAccess access, RedemptionCodeResolver resolver, IClaimRepository claims, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<RedeemCodeCommand, RedemptionPreviewDto>
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

        var resolved = await resolver.ResolveAsync(request.BusinessId, request.Code, cancellationToken).ConfigureAwait(false);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var claim = resolved.Value;
        var now = clock.UtcNow;
        if (!claim.IsRedeemableAt(now))
        {
            return RedemptionCodeResolver.NotRedeemable(claim, now);
        }

        var staffId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        claim.Redeem(staffId, now);
        if (!await claims.TryTransitionAsync(claim, ClaimStatus.Reserved, cancellationToken).ConfigureAwait(false))
        {
            // Başqa işçi/cəhd bizi qabaqladı (və ya rezerv elə indi bağlandı): hadisə yazılmır.
            return SocialErrors.AlreadyRedeemed;
        }

        return await resolver.PreviewAsync(claim, now, cancellationToken).ConfigureAwait(false);
    }
}
