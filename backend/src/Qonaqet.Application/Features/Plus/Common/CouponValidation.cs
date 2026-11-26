using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Domain.Plus;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Plus.Common;

public sealed record CouponValidationDto(Guid CouponId, string Title, CouponCodeType CodeType, string Code, DateTimeOffset ValidatedAt);

/// <summary>
/// Brendin kupon kodunu yoxlayır (brend paneli və Partner API eyni qaydanı işlədir). Yalnız həmin brendin kuponlarında axtarılır.
/// <list type="bullet">
/// <item><c>Unique</c> kod: istifadəçiyə verilmiş olmalıdır; tək şərtli atomik yeniləmə ilə istifadə olunmuş kimi işarələnir, təkrar yoxlama
/// (paralel də) <c>already_redeemed</c> qaytarır.</item>
/// <item><c>Shared</c> kod: hamı üçün eynidir, ona görə "istifadə olunub" işarəsi qoyulmur — yalnız kuponun etibarlı olduğu təsdiqlənir.</item>
/// </list>
/// Hər uğurlu yoxlama audit log-a yazılır. Cavabda kodun kimə verildiyi yoxdur (brend müştərinin şəxsi məlumatını görmür).
/// </summary>
public sealed class CouponValidation(ICouponRepository coupons, ICouponCodeRepository codes, IOutbox outbox, IClock clock)
{
    public const int CouponLimit = 500;

    /// <param name="couponId">Verilibsə axtarış yalnız bu kuponla məhdudlaşır (Partner API <c>{couponId, code}</c>).</param>
    /// <param name="actorType">Audit üçün: <c>User</c> (brend işçisi) və ya <c>IntegrationClient</c>.</param>
    public async Task<Result<CouponValidationDto>> ValidateAsync(
        BrandId brandId, string rawCode, CouponId? couponId, Guid actorId, string actorType, CancellationToken cancellationToken)
    {
        var code = CouponCodes.Normalize(rawCode);
        if (code is null)
        {
            return PlusErrors.CouponCodeInvalid;
        }

        var now = clock.UtcNow;
        var brandCoupons = (await coupons.ListAsync(brandId, CouponLimit, cancellationToken).ConfigureAwait(false))
            .Where(c => couponId is null || c.Id == couponId).ToList();

        var shared = brandCoupons.FirstOrDefault(c => c.CodeType == CouponCodeType.Shared && c.SharedCode == code);
        if (shared is not null)
        {
            if (!shared.IsAvailableAt(now))
            {
                return PlusErrors.CouponExpired;
            }

            await AuditAsync(actorId, actorType, now, nameof(Coupon), shared.Id.Value, shared.Id, cancellationToken).ConfigureAwait(false);
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

        await AuditAsync(actorId, actorType, now, nameof(CouponCode), unique.Id.Value, coupon.Id, cancellationToken).ConfigureAwait(false);
        return new CouponValidationDto(coupon.Id.Value, coupon.Title, coupon.CodeType, code, now);
    }

    private Task AuditAsync(
        Guid actorId, string actorType, DateTimeOffset now, string entityType, Guid entityId, CouponId couponId, CancellationToken cancellationToken) =>
        outbox.AddAsync(
            [new AuditRecorded(now, actorId, actorType, PlusAuditActions.CouponCodeRedeem, entityType, entityId,
                new Dictionary<string, string?> { ["couponId"] = couponId.Value.ToString() })],
            cancellationToken);
}
