using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Integration.Common;
using Qonaqet.Application.Features.Social.Common;
using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Integration;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Integration.Commands.PartnerRedeem;

/// <summary>
/// <c>POST /partner/v1/redemptions</c> (<c>redemptions:write</c>): kassa sistemi kodu təhvil verir — işçi təhvili ilə <b>eyni</b> qaydalar
/// (<see cref="CodeRedemption"/>): claim bir dəfə, hədiyyədə sətirlər və ya məbləğ (qismən təhvil daxil), şəbəkə vauçeri yalnız qəbul edən
/// biznesdə. Client yalnız öz <c>business_ids</c>-indəki biznes adından işləyir. Təhvil edən kimi client-in ID-si yazılır. Təkrar sorğunu
/// <c>Idempotency-Key</c> filtri tutur. Cavabda şəxsi məlumat yoxdur.
/// </summary>
[RequiresRole(PartnerScopes.RedemptionsWrite)]
public sealed record PartnerRedeemCommand(
    Guid BusinessId, string Code, IReadOnlyList<GiftLine>? Lines, long? AmountQepik, string? ExternalReference)
    : ICommand<PartnerRedemptionDto>, INonTransactionalCommand
{
    public const int ExternalReferenceMaxLength = 100;

    /// <summary>Kod bearer-dir: loga düşməsin.</summary>
    public override string ToString() => nameof(PartnerRedeemCommand);
}

/// <param name="RedemptionId">Hədiyyədə təhvil qeydinin, claim-də claim-in ID-si (geri alma üçün).</param>
/// <param name="ExternalReference">Partnyorun öz çek/sifariş nömrəsi — olduğu kimi qaytarılır.</param>
public sealed record PartnerRedemptionDto(Guid RedemptionId, string? ExternalReference, RedemptionPreviewDto Redemption);

public sealed class PartnerRedeemValidator : Validator<PartnerRedeemCommand>
{
    public PartnerRedeemValidator()
    {
        Rule("code", x => !string.IsNullOrWhiteSpace(x.Code) && x.Code.Length <= 200, "code_invalid", "Kod boş ola bilməz.");
        Rule("lines", x => x.Lines is null || x.Lines.Count <= GiftRules.MaxLines, GiftErrors.SelectionInvalid.Code, GiftErrors.SelectionInvalid.Message);
        Rule(
            "externalReference",
            x => x.ExternalReference is null || x.ExternalReference.Length <= PartnerRedeemCommand.ExternalReferenceMaxLength,
            "external_reference_invalid",
            "externalReference ən çox 100 simvol ola bilər.");
    }
}

public sealed class PartnerRedeemHandler(PartnerAccess access, CodeRedemption redemption) : ICommandHandler<PartnerRedeemCommand, PartnerRedemptionDto>
{
    public async Task<Result<PartnerRedemptionDto>> Handle(PartnerRedeemCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var granted = await access.RequireBusinessAsync(request.BusinessId, cancellationToken).ConfigureAwait(false);
        if (granted.IsFailure)
        {
            return granted.Error;
        }

        var (client, business) = granted.Value;
        var redeemed = await redemption.RedeemAsync(business, request.Code, request.Lines, request.AmountQepik, client.Id.Value, cancellationToken)
            .ConfigureAwait(false);
        return redeemed.IsFailure
            ? redeemed.Error
            : new PartnerRedemptionDto(redeemed.Value.RedemptionId ?? redeemed.Value.Id, request.ExternalReference, redeemed.Value);
    }
}
