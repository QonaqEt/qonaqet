using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Plus.Queries.GetCoupons;
using Qonaqet.Domain.Plus;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Plus.Commands.RevealCoupon;

/// <summary>
/// <c>POST coupons/{id}/reveal</c>: istifadəçi kuponun kodunu açır.
/// <list type="bullet">
/// <item><c>Shared</c>: hamıya eyni kod qaytarılır (heç nə yazılmır, təkrar açılış eyni kodu verir).</item>
/// <item><c>Unique</c>: boş kodlardan biri tək şərtli atomik yeniləmə ilə istifadəçiyə verilir, ona görə eyni kod iki istifadəçiyə düşmür.
/// İstifadəçinin n-ci kodu "yer" n-dir; (<c>couponId</c>, <c>assignedUserId</c>, <c>slot</c>) unikal indeksi paralel açılışlarda da
/// <c>perUserLimit</c>-in aşılmasına imkan vermir → <c>limit_reached</c>. Boş kod qalmayıbsa <c>coupon_sold_out</c>.</item>
/// </list>
/// Tranzaksiyasızdır: yeganə yazı tək sənədli atomik yeniləmədir.
/// </summary>
[RequiresActiveAccount]
public sealed record RevealCouponCommand(CouponId CouponId) : ICommand<RevealedCouponDto>, INonTransactionalCommand;

public sealed record RevealedCouponDto(Guid CouponId, CouponCodeType CodeType, string Code, int RevealedCount, int PerUserLimit);

public sealed class RevealCouponHandler(
    ICouponRepository coupons, IBrandRepository brands, ICouponCodeRepository codes, PlusCatalog catalog, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<RevealCouponCommand, RevealedCouponDto>
{
    public const int MaxAttempts = 3;

    public async Task<Result<RevealedCouponDto>> Handle(RevealCouponCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        var now = clock.UtcNow;
        var coupon = await coupons.GetAsync(request.CouponId, cancellationToken).ConfigureAwait(false);
        var brand = coupon is null ? null : await brands.GetAsync(coupon.BrandId, cancellationToken).ConfigureAwait(false);
        if (coupon is null || brand is not { IsActive: true } || !coupon.IsActive)
        {
            return PlusErrors.CouponNotFound;
        }

        if (!coupon.IsAvailableAt(now))
        {
            return PlusErrors.CouponExpired;
        }

        if (!coupon.IsOpenTo(await catalog.CategoriesAsync(userId, now, cancellationToken).ConfigureAwait(false)))
        {
            return PlusErrors.CouponNotEligible;
        }

        if (coupon.CodeType == CouponCodeType.Shared)
        {
            return new RevealedCouponDto(coupon.Id.Value, coupon.CodeType, coupon.SharedCode!, 1, coupon.PerUserLimit);
        }

        // Paralel açılış eyni "yeri" tutubsa, say yenidən oxunur və növbəti yer sınanır (limit dolana qədər).
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var mine = await codes.ListAssignedAsync(userId, [coupon.Id], cancellationToken).ConfigureAwait(false);
            if (mine.Count >= coupon.PerUserLimit)
            {
                return PlusErrors.LimitReached;
            }

            var slot = mine.Count + 1;
            var (outcome, code) = await codes.TryAssignAsync(coupon.Id, userId, slot, now, cancellationToken).ConfigureAwait(false);
            switch (outcome)
            {
                case CouponCodeAssignment.Assigned:
                    return new RevealedCouponDto(coupon.Id.Value, coupon.CodeType, code!.Code, slot, coupon.PerUserLimit);
                case CouponCodeAssignment.SoldOut:
                    return PlusErrors.SoldOut;
            }
        }

        return PlusErrors.LimitReached;
    }
}
