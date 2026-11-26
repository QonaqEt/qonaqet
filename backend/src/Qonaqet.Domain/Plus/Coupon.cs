using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Plus;

public readonly record struct CouponId(Guid Value) : IStronglyTypedId<CouponId>
{
    public static CouponId From(Guid value) => new(value);
}

/// <summary>
/// <see cref="Shared"/>: hamıya eyni kod (kuponun özündə saxlanılır). <see cref="Unique"/>: hər istifadəçiyə ayrıca kod — <c>couponCodes</c>-dan atomik
/// təyin olunur.
/// </summary>
public enum CouponCodeType
{
    Shared,
    Unique,
}

/// <summary>Kuponun dəyişdirilə bilən sahələri. Kod növü yaradılanda seçilir və sonra dəyişmir.</summary>
/// <param name="EligibleCategoryIds">Boşdursa kupon hamıya açıqdır; əks halda yalnız bu kateqoriyalardan birinə aid istifadəçilərə.</param>
/// <param name="PerUserLimit">Bir istifadəçiyə verilə bilən unikal kod sayı.</param>
public sealed record CouponDetails(
    string Title,
    string? Description,
    Guid? ImageFileId,
    IReadOnlyList<Guid> EligibleCategoryIds,
    int PerUserLimit,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil,
    bool IsActive);

/// <summary>
/// QONAQET Plus kuponu (<c>coupons</c>): brendin endirimi, hamıya və ya seçilmiş kateqoriyalara. Kateqoriyalar yalnız id ilə tutulur — kod üzrə heç
/// bir şaxələnmə yoxdur.
/// </summary>
public sealed class Coupon : AggregateRoot<CouponId>
{
    public const int TitleMaxLength = 120;

    public const int DescriptionMaxLength = 1000;

    public const int MaxPerUserLimit = 100;

    public const int MaxEligibleCategories = 20;

    private List<Guid> _eligibleCategoryIds = [];

    private Coupon(CouponId id, DateTimeOffset createdAt, Guid? createdBy)
        : base(id, createdAt, createdBy)
    {
    }

    private Coupon()
    {
    }

    public BrandId BrandId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public Guid? ImageFileId { get; private set; }

    public IReadOnlyList<Guid> EligibleCategoryIds
    {
        get => _eligibleCategoryIds;
        private set => _eligibleCategoryIds = [.. value];
    }

    public CouponCodeType CodeType { get; private set; }

    /// <summary>Yalnız <see cref="CouponCodeType.Shared"/>: hamıya verilən kod.</summary>
    public string? SharedCode { get; private set; }

    public int PerUserLimit { get; private set; }

    public DateTimeOffset ValidFrom { get; private set; }

    public DateTimeOffset ValidUntil { get; private set; }

    public bool IsActive { get; private set; }

    public static Coupon Create(BrandId brandId, CouponCodeType codeType, string? sharedCode, CouponDetails details, DateTimeOffset at, Guid? by)
    {
        if ((codeType == CouponCodeType.Shared) != (sharedCode is not null))
        {
            throw new ArgumentException("Ortaq kod yalnız Shared kuponda olur və orada mütləqdir.", nameof(sharedCode));
        }

        var coupon = new Coupon(StronglyTypedId.New<CouponId>(), Guard.Utc(at), by)
        {
            BrandId = brandId,
            CodeType = codeType,
            SharedCode = sharedCode is null ? null : CouponCodes.Normalize(sharedCode) ?? throw new ArgumentException("Kod düzgün deyil.", nameof(sharedCode)),
        };
        coupon.Apply(details);
        return coupon;
    }

    public void Update(CouponDetails details, string? sharedCode, DateTimeOffset at, Guid? by)
    {
        if (CodeType == CouponCodeType.Shared && sharedCode is not null)
        {
            SharedCode = CouponCodes.Normalize(sharedCode) ?? throw new ArgumentException("Kod düzgün deyil.", nameof(sharedCode));
        }

        Apply(details);
        MarkUpdated(at, by);
    }

    /// <summary>Aktivdir və etibarlılıq müddətindədir.</summary>
    public bool IsAvailableAt(DateTimeOffset now) => IsActive && ValidFrom <= now && now < ValidUntil;

    /// <summary>Hamıya açıqdır və ya istifadəçinin kateqoriyalarından biri uyğundur.</summary>
    public bool IsOpenTo(IReadOnlyCollection<Guid> userCategoryIds)
    {
        ArgumentNullException.ThrowIfNull(userCategoryIds);
        return _eligibleCategoryIds.Count == 0 || _eligibleCategoryIds.Exists(userCategoryIds.Contains);
    }

    public static bool IsValid(CouponDetails? details) =>
        details is not null
        && !string.IsNullOrWhiteSpace(details.Title) && details.Title.Trim().Length <= TitleMaxLength
        && (details.Description is null || details.Description.Length <= DescriptionMaxLength)
        && details.EligibleCategoryIds is not null && details.EligibleCategoryIds.Count <= MaxEligibleCategories
        && details.PerUserLimit is >= 1 and <= MaxPerUserLimit
        && details.ValidFrom < details.ValidUntil;

    private void Apply(CouponDetails details)
    {
        if (!IsValid(details))
        {
            throw new ArgumentException("Kupon sahələri düzgün deyil.", nameof(details));
        }

        Title = details.Title.Trim();
        Description = string.IsNullOrWhiteSpace(details.Description) ? null : details.Description.Trim();
        ImageFileId = details.ImageFileId;
        _eligibleCategoryIds = [.. details.EligibleCategoryIds.Distinct()];
        PerUserLimit = details.PerUserLimit;
        ValidFrom = Guard.Utc(details.ValidFrom);
        ValidUntil = Guard.Utc(details.ValidUntil);
        IsActive = details.IsActive;
    }
}

/// <summary>Kupon kodlarının forması: 3–40 simvol, böyük latın hərfləri, rəqəmlər və <c>-</c>.</summary>
public static class CouponCodes
{
    public const int MinLength = 3;

    public const int MaxLength = 40;

    /// <summary>Boşluqlar atılır, böyük hərfə çevrilir; forma uyğun deyilsə <c>null</c>.</summary>
    public static string? Normalize(string? code)
    {
        var value = code?.Trim().ToUpperInvariant();
        return value is { Length: >= MinLength and <= MaxLength } && value.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '-')
            ? value
            : null;
    }
}
