using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Domain.Plus;

namespace Qonaqet.Application.Features.Plus.Common;

public sealed record BrandDto(Guid Id, string Name, Guid? LogoFileId, string? Website, string ContactEmail, bool IsActive);

/// <summary>Admin və brend görünüşü: kodun özü (ortaq kod istisna) və kimə verildiyi burada yoxdur — yalnız saylar.</summary>
public sealed record CouponDto(
    Guid Id,
    Guid BrandId,
    string Title,
    string? Description,
    Guid? ImageFileId,
    IReadOnlyList<Guid> EligibleCategoryIds,
    CouponCodeType CodeType,
    string? SharedCode,
    int PerUserLimit,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil,
    bool IsActive,
    CouponCodeCounts? Codes);

public static class PlusMapping
{
    public static BrandDto ToDto(this Brand brand)
    {
        ArgumentNullException.ThrowIfNull(brand);
        return new BrandDto(brand.Id.Value, brand.Name, brand.LogoFileId, brand.Website, brand.ContactEmail, brand.IsActive);
    }

    public static CouponDto ToDto(this Coupon coupon, CouponCodeCounts? codes = null)
    {
        ArgumentNullException.ThrowIfNull(coupon);
        return new CouponDto(
            coupon.Id.Value, coupon.BrandId.Value, coupon.Title, coupon.Description, coupon.ImageFileId, coupon.EligibleCategoryIds, coupon.CodeType,
            coupon.SharedCode, coupon.PerUserLimit, coupon.ValidFrom, coupon.ValidUntil, coupon.IsActive, codes);
    }
}

public static class PlusAuditActions
{
    public const string BrandCreate = "BrandCreate";
    public const string BrandUpdate = "BrandUpdate";
    public const string BrandAssignUser = "BrandAssignUser";
    public const string CouponCreate = "CouponCreate";
    public const string CouponUpdate = "CouponUpdate";
    public const string CouponCodesAdd = "CouponCodesAdd";
    public const string CouponCodeRedeem = "CouponCodeRedeem";
}
