using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Integration.Common;
using Qonaqet.Application.Features.Plus.Common;
using Qonaqet.Domain.Integration;
using Qonaqet.Domain.Plus;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Integration.Commands.PartnerValidateCoupon;

/// <summary>
/// <c>POST /partner/v1/coupons/validate</c> (<c>coupons:validate</c>, E13-T12): brendə bağlı client (<c>ownerType = Brand</c>) müştərinin kodunu
/// yoxlayır və unikal kodu istifadə olunmuş kimi işarələyir — brend panelindəki ilə eyni qaydalar (<see cref="CouponValidation"/>), yalnız
/// <paramref name="CouponId"/> kuponunda və yalnız client-in brendində. <c>Idempotency-Key</c> məcburidir.
/// </summary>
[RequiresRole(PartnerScopes.CouponsValidate)]
public sealed record PartnerValidateCouponCommand(Guid CouponId, string Code) : ICommand<CouponValidationDto>, INonTransactionalCommand;

public sealed class PartnerValidateCouponValidator : Validator<PartnerValidateCouponCommand>
{
    public PartnerValidateCouponValidator()
    {
        Rule("couponId", x => x.CouponId != Guid.Empty, ValidationCodes.Required, "couponId məcburidir.");
        Rule("code", x => CouponCodes.Normalize(x.Code) is not null, PlusErrors.CouponCodeInvalid.Code, PlusErrors.CouponCodeInvalid.Message);
    }
}

public sealed class PartnerValidateCouponHandler(PartnerAccess access, CouponValidation validation)
    : ICommandHandler<PartnerValidateCouponCommand, CouponValidationDto>
{
    public async Task<Result<CouponValidationDto>> Handle(PartnerValidateCouponCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var client = await access.RequireAsync(cancellationToken).ConfigureAwait(false);
        if (client.IsFailure)
        {
            return client.Error;
        }

        if (client.Value.OwnerType != IntegrationOwnerType.Brand)
        {
            return IntegrationErrors.BrandClientRequired;
        }

        return await validation.ValidateAsync(
                BrandId.From(client.Value.OwnerId), request.Code, CouponId.From(request.CouponId), client.Value.Id.Value, "IntegrationClient",
                cancellationToken)
            .ConfigureAwait(false);
    }
}
