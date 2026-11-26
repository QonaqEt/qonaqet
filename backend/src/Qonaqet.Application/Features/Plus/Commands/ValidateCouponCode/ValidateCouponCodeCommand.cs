using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Plus.Common;
using Qonaqet.Domain.Plus;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Plus.Commands.ValidateCouponCode;

/// <summary>
/// <c>POST brand/coupons/validate</c>: brend istifadəçisi müştərinin kodunu yoxlayır. Yalnız öz brendinin kuponlarında axtarılır; qaydalar
/// <see cref="CouponValidation"/>-dadır (Partner API ilə eyni).
/// </summary>
[RequiresRole(RoleNames.BrandUser)]
public sealed record ValidateCouponCodeCommand(string Code) : ICommand<CouponValidationDto>, INonTransactionalCommand;

public sealed class ValidateCouponCodeValidator : Validator<ValidateCouponCodeCommand>
{
    public ValidateCouponCodeValidator() =>
        Rule("code", x => CouponCodes.Normalize(x.Code) is not null, PlusErrors.CouponCodeInvalid.Code, PlusErrors.CouponCodeInvalid.Message);
}

public sealed class ValidateCouponCodeHandler(BrandAccess access, CouponValidation validation, ICurrentUser currentUser)
    : ICommandHandler<ValidateCouponCodeCommand, CouponValidationDto>
{
    public async Task<Result<CouponValidationDto>> Handle(ValidateCouponCodeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var brand = await access.RequireAsync(cancellationToken).ConfigureAwait(false);
        if (brand.IsFailure)
        {
            return brand.Error;
        }

        var actorId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        return await validation.ValidateAsync(brand.Value.Id, request.Code, null, actorId, "User", cancellationToken).ConfigureAwait(false);
    }
}
