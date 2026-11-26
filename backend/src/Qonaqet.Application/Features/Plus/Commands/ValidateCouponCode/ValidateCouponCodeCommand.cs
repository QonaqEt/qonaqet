using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
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
/// <c>POST brand/coupons/validate</c>: brend istifadəçisi müştərinin kodunu yoxlayır. Yalnız öz brendinin kuponlarında axtarılır.
/// <list type="bullet">
/// <item><c>Unique</c> kod: istifadəçiyə verilmiş olmalıdır; tək şərtli atomik yeniləmə ilə istifadə olunmuş kimi işarələnir, təkrar yoxlama
/// (paralel də) <c>already_redeemed</c> qaytarır.</item>
/// <item><c>Shared</c> kod: hamı üçün eynidir, ona görə "istifadə olunub" işarəsi qoyulmur — yalnız kuponun etibarlı olduğu təsdiqlənir.</item>
/// </list>
/// Hər uğurlu yoxlama audit log-a yazılır. Cavabda kodun kimə verildiyi yoxdur (brend müştərinin şəxsi məlumatını görmür).
/// </summary>
[RequiresRole(RoleNames.BrandUser)]
public sealed record ValidateCouponCodeCommand(string Code) : ICommand<CouponValidationDto>, INonTransactionalCommand;

public sealed record CouponValidationDto(Guid CouponId, string Title, CouponCodeType CodeType, string Code, DateTimeOffset ValidatedAt);

public sealed class ValidateCouponCodeValidator : Validator<ValidateCouponCodeCommand>
{
    public ValidateCouponCodeValidator() =>
        Rule("code", x => CouponCodes.Normalize(x.Code) is not null, PlusErrors.CouponCodeInvalid.Code, PlusErrors.CouponCodeInvalid.Message);
}

public sealed class ValidateCouponCodeHandler(
    BrandAccess access, ICouponRepository coupons, ICouponCodeRepository codes, ICurrentUser currentUser, IOutbox outbox, IClock clock)
    : ICommandHandler<ValidateCouponCodeCommand, CouponValidationDto>
{
    public const int CouponLimit = 500;

    public async Task<Result<CouponValidationDto>> Handle(ValidateCouponCodeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var brand = await access.RequireAsync(cancellationToken).ConfigureAwait(false);
        if (brand.IsFailure)
        {
            return brand.Error;
        }

        var actorId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        var now = clock.UtcNow;
        var code = CouponCodes.Normalize(request.Code)!;
        var brandCoupons = await coupons.ListAsync(brand.Value.Id, CouponLimit, cancellationToken).ConfigureAwait(false);

        var shared = brandCoupons.FirstOrDefault(c => c.CodeType == CouponCodeType.Shared && c.SharedCode == code);
        if (shared is not null)
        {
            if (!shared.IsAvailableAt(now))
            {
                return PlusErrors.CouponExpired;
            }

            await AuditAsync(actorId, now, nameof(Coupon), shared.Id.Value, shared.Id, cancellationToken).ConfigureAwait(false);
            return new CouponValidationDto(shared.Id.Value, shared.Title, shared.CodeType, code, now);
        }

        var unique = await codes.FindByCodeAsync([.. brandCoupons.Where(c => c.CodeType == CouponCodeType.Unique).Select(c => c.Id)], code, cancellationToken)
            .ConfigureAwait(false);
        if (unique is null || unique.AssignedUserId is null)
        {
            return PlusErrors.CodeNotFound;
        }

        if (unique.UsedAt is not null)
        {
            return PlusErrors.AlreadyRedeemed;
        }

        var coupon = brandCoupons.First(c => c.Id == unique.CouponId);
        if (!coupon.IsAvailableAt(now))
        {
            return PlusErrors.CouponExpired;
        }

        if (!await codes.TryMarkUsedAsync(unique.Id, actorId, now, cancellationToken).ConfigureAwait(false))
        {
            return PlusErrors.AlreadyRedeemed;
        }

        await AuditAsync(actorId, now, nameof(CouponCode), unique.Id.Value, coupon.Id, cancellationToken).ConfigureAwait(false);
        return new CouponValidationDto(coupon.Id.Value, coupon.Title, coupon.CodeType, code, now);
    }

    private Task AuditAsync(Guid actorId, DateTimeOffset now, string entityType, Guid entityId, CouponId couponId, CancellationToken cancellationToken) =>
        outbox.AddAsync(
            [new AuditRecorded(now, actorId, "User", PlusAuditActions.CouponCodeRedeem, entityType, entityId,
                new Dictionary<string, string?> { ["couponId"] = couponId.Value.ToString() })],
            cancellationToken);
}
