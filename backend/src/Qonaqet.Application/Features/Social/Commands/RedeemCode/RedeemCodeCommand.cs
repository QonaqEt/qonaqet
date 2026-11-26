using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Businesses.Common;
using Qonaqet.Application.Features.Gifts.Common;
using Qonaqet.Application.Features.Social.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Gifts;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Social.Commands.RedeemCode;

/// <summary>
/// <c>POST business/{businessId}/redeem</c>: işçi kodu təhvil verir.
/// Qaydalar <see cref="CodeRedemption"/>-dadır (Partner API ilə ortaq): claim-də şərtli status keçidi (<c>already_redeemed</c>), hədiyyədə seçilən
/// sətirlər (<paramref name="Lines"/>) və ya məbləğ (<paramref name="AmountQepik"/>, qismən ola bilər). Əmrin özü tranzaksiyasızdır: hər budaq öz
/// tranzaksiyasını açır (hədiyyə toqquşmada təzə tranzaksiya ilə təkrar cəhd edir).
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

public sealed class RedeemCodeHandler(BusinessAccess access, CodeRedemption redemption, ICurrentUser currentUser)
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

        var staffId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        return await redemption.RedeemAsync(grant.Value.Business, request.Code, request.Lines, request.AmountQepik, staffId, cancellationToken)
            .ConfigureAwait(false);
    }
}
