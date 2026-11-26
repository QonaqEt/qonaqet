using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Social.Common;
using Qonaqet.Domain.Plus;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Plus.Queries.GetCoupons;

/// <summary>
/// <c>GET coupons</c>: istifadəçinin bu anda görə biləcəyi kuponlar — aktiv, etibarlı, brendi aktiv və hamıya açıq və ya istifadəçinin
/// kateqoriyalarından birinə uyğun. Ortaq kod burada göstərilmir (açılışla verilir); istifadəçiyə artıq verilmiş unikal kodlar göstərilir.
/// Uyğunluq yalnız kateqoriya id-ləri ilə yoxlanılır.
/// </summary>
public sealed record GetCouponsQuery : IQuery<IReadOnlyList<UserCouponDto>>;

/// <param name="MyCodes">İstifadəçiyə verilmiş unikal kodlar (ortaq kuponda boşdur).</param>
/// <param name="CanReveal">Yeni kod açmaq mümkündürmü: ortaq kuponda həmişə; unikalda limit dolmayıb və boş kod qalıb.</param>
public sealed record UserCouponDto(
    Guid Id,
    Guid BrandId,
    string BrandName,
    Guid? BrandLogoFileId,
    string Title,
    string? Description,
    Guid? ImageFileId,
    CouponCodeType CodeType,
    int PerUserLimit,
    DateTimeOffset ValidUntil,
    IReadOnlyList<string> MyCodes,
    bool CanReveal);

public sealed class GetCouponsHandler(PlusCatalog catalog, ICouponCodeRepository codes, ICurrentUser currentUser, IClock clock)
    : IQueryHandler<GetCouponsQuery, IReadOnlyList<UserCouponDto>>
{
    public const int Limit = 200;

    public async Task<Result<IReadOnlyList<UserCouponDto>>> Handle(GetCouponsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        var visible = await catalog.VisibleAsync(userId, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        var mine = (await codes.ListAssignedAsync(userId, [.. visible.Select(v => v.Coupon.Id)], cancellationToken).ConfigureAwait(false))
            .ToLookup(c => c.CouponId, c => c.Code);

        var result = new List<UserCouponDto>(visible.Count);
        foreach (var (coupon, brand) in visible)
        {
            var myCodes = mine[coupon.Id].ToList();
            var canReveal = coupon.CodeType == CouponCodeType.Shared
                || (myCodes.Count < coupon.PerUserLimit && await codes.HasFreeAsync(coupon.Id, cancellationToken).ConfigureAwait(false));
            result.Add(new UserCouponDto(
                coupon.Id.Value, brand.Id.Value, brand.Name, brand.LogoFileId, coupon.Title, coupon.Description, coupon.ImageFileId, coupon.CodeType,
                coupon.PerUserLimit, coupon.ValidUntil, myCodes, canReveal));
        }

        return result;
    }
}

/// <summary>İstifadəçiyə görünən kuponlar və onların brendləri (ortaq məntiq: siyahı və açılış).</summary>
public sealed class PlusCatalog(ICouponRepository coupons, IBrandRepository brands, IUserRepository users, SocialEligibility eligibility)
{
    public async Task<IReadOnlyList<(Coupon Coupon, Brand Brand)>> VisibleAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var categories = await CategoriesAsync(userId, now, cancellationToken).ConfigureAwait(false);
        var available = await coupons.ListAvailableAsync(now, GetCouponsHandler.Limit, cancellationToken).ConfigureAwait(false);
        var brandCache = new Dictionary<BrandId, Brand?>();
        var result = new List<(Coupon, Brand)>();
        foreach (var coupon in available.Where(c => c.IsOpenTo(categories)))
        {
            if (!brandCache.TryGetValue(coupon.BrandId, out var brand))
            {
                brand = await brands.GetAsync(coupon.BrandId, cancellationToken).ConfigureAwait(false);
                brandCache[coupon.BrandId] = brand;
            }

            if (brand is { IsActive: true })
            {
                result.Add((coupon, brand));
            }
        }

        return result;
    }

    /// <summary>İstifadəçinin bu anda aid olduğu aktiv kateqoriyaların id-ləri.</summary>
    public async Task<IReadOnlyCollection<Guid>> CategoriesAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var user = await users.GetAsync(UserId.From(userId), cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Cari istifadəçi tapılmadı.");
        return [.. (await eligibility.EligibleCategoriesAsync(user, now, cancellationToken).ConfigureAwait(false)).Select(c => c.Id.Value)];
    }
}
