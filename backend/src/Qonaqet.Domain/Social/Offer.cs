using System.Globalization;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Catalog;
using Qonaqet.Domain.Categories;
using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Social;

public readonly record struct OfferId(Guid Value) : IStronglyTypedId<OfferId>
{
    public static OfferId From(Guid value) => new(value);
}

/// <summary>Sosial qonaqlıq təklifinin növü.</summary>
public enum OfferType
{
    /// <summary>Biznesin gündəlik pulsuz təklifi (kvota hər Bakı günü yenilənir). Komissiya yoxdur.</summary>
    BusinessDaily,

    /// <summary>"Askıda": kassada kiminsə qabaqcadan ödədiyi məhsullar (say ilə). Komissiya yoxdur.</summary>
    Suspended,

    /// <summary>Onlayn ödənişlə kateqoriya üçün alınmış məhsullar (E08).</summary>
    Remote,
}

/// <summary>Ianəçinin (və ya biznesin) alıcıya necə göstərildiyi.</summary>
public enum DonorDisplay
{
    Anonymous,
    Named,
}

/// <summary>
/// Sosial qonaqlıq təklifi (<c>offers</c>): bir biznesdə, bir kateqoriya üçün. Alıcını nə biznes, nə ianəçi seçir — təklifdən
/// kateqoriyaya uyğun istənilən istifadəçi claim edir. Vahidin götürülməsi və geri qaytarılması aqreqat üzərindən yox,
/// repository-də tək şərtli atomik yeniləmə ilə olur (<c>remaining &gt; 0</c> / <c>usedToday &lt; dailyQuota</c>), ona görə
/// paralel claim-lər kvotanı heç vaxt aşmır. Silmə həmişə soft delete-dir.
/// </summary>
public sealed class Offer : AggregateRoot<OfferId>
{
    public const int TitleMaxLength = 120;
    public const int DonorNameMaxLength = 60;
    public const int MaxDailyQuota = 1000;
    public const int MaxSuspendedQuantity = 1000;

    private Offer(OfferId id, BusinessId businessId, OfferType type, CategoryId categoryId, DateTimeOffset createdAt, Guid? createdBy)
        : base(id, createdAt, createdBy)
    {
        BusinessId = businessId;
        Type = type;
        CategoryId = categoryId;
    }

    private Offer()
    {
    }

    public BusinessId BusinessId { get; private set; }

    public OfferType Type { get; private set; }

    public string Title { get; private set; } = string.Empty;

    /// <summary>Təklifin aid olduğu kateqoriya (yalnız ID ilə; heç vaxt kod üzrə şaxələnmə yoxdur).</summary>
    public CategoryId CategoryId { get; private set; }

    /// <summary>Kataloq elementi (istəyə bağlı): alıcıya məhsulun şəkli/təsviri göstərilə bilər.</summary>
    public CatalogItemId? CatalogItemId { get; private set; }

    public DonorDisplay DonorDisplay { get; private set; }

    /// <summary><see cref="DonorDisplay.Named"/> olduqda göstərilən ad (gündəlik təklifdə boşdursa biznesin adı).</summary>
    public string? DonorName { get; private set; }

    /// <summary>Təklif claim üçün açıqdır (biznes müvəqqəti dayandıra bilər).</summary>
    public bool IsActive { get; private set; }

    // ── BusinessDaily ──

    public int DailyQuota { get; private set; }

    /// <summary><see cref="QuotaDate"/> günü üçün götürülmüş vahidlər. Başqa gündə məntiqi olaraq 0-dır (lazy sıfırlama).</summary>
    public int UsedToday { get; private set; }

    /// <summary><see cref="UsedToday"/>-in aid olduğu Bakı günü (<c>yyyy-MM-dd</c>); heç claim olmayıbsa <c>null</c>.</summary>
    public string? QuotaDate { get; private set; }

    /// <summary>Gün ərzində claim pəncərəsi (<c>HH:mm</c>, Bakı vaxtı); ikisi də boşdursa bütün gün.</summary>
    public string? AvailableFrom { get; private set; }

    public string? AvailableTo { get; private set; }

    // ── Suspended / Remote ──

    public int TotalQuantity { get; private set; }

    public int Remaining { get; private set; }

    /// <summary>Askıda üçün ödəyən QONAQET istifadəçisi (məlumdursa); yalnız aqreqatları görür, alıcını yox.</summary>
    public Guid? DonorUserId { get; private set; }

    /// <summary>Askıda qeydini kassada daxil edən işçi.</summary>
    public Guid? CreatedByStaffId { get; private set; }

    /// <summary>Bakı günü açarı (<see cref="QuotaDate"/> formatı).</summary>
    public static string DateKey(DateOnly bakuDate) => bakuDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static bool IsValidDailyQuota(int quota) => quota is >= 1 and <= MaxDailyQuota;

    public static bool IsValidQuantity(int quantity) => quantity is >= 1 and <= MaxSuspendedQuantity;

    /// <summary>Pəncərə ya tam boşdur, ya da iki düzgün <c>HH:mm</c> fərqli vaxtdır (to &lt; from gecəyarısını keçir).</summary>
    public static bool IsValidWindow(string? from, string? to) =>
        (from is null && to is null) || (BusinessRules.IsClockTime(from) && BusinessRules.IsClockTime(to) && from != to);

    public static Offer CreateDaily(BusinessId businessId, CategoryId categoryId, DailyOfferDetails details, DateTimeOffset at, Guid? by)
    {
        ArgumentNullException.ThrowIfNull(details);
        var offer = new Offer(StronglyTypedId.New<OfferId>(), businessId, OfferType.BusinessDaily, categoryId, at, by);
        offer.ApplyDaily(details);
        return offer;
    }

    public static Offer CreateSuspended(
        BusinessId businessId, CategoryId categoryId, SuspendedOfferDetails details, Guid staffUserId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (!IsValidQuantity(details.Quantity))
        {
            throw new ArgumentOutOfRangeException(nameof(details), "Say düzgün deyil.");
        }

        var offer = new Offer(StronglyTypedId.New<OfferId>(), businessId, OfferType.Suspended, categoryId, at, staffUserId)
        {
            TotalQuantity = details.Quantity,
            Remaining = details.Quantity,
            DonorUserId = details.DonorUserId,
            CreatedByStaffId = Guard.NotEmpty(staffUserId),
            IsActive = true,
        };
        offer.ApplyCommon(details.Title, details.CatalogItemId, details.DonorDisplay, details.DonorName);
        return offer;
    }

    /// <summary>Gündəlik təklifin parametrlərini dəyişir. Bugünkü sayğac saxlanılır; yeni kvota ondan azdırsa bu gün yeni claim olmur.</summary>
    public void UpdateDaily(DailyOfferDetails details, DateTimeOffset at, Guid? by)
    {
        ArgumentNullException.ThrowIfNull(details);
        EnsureType(OfferType.BusinessDaily);
        ApplyDaily(details);
        MarkUpdated(at, by);
    }

    /// <summary>Askıda təklifinin başlığı/göstərilməsi və aktivliyi (say artırılmır: yeni askıda ayrıca qeyddir).</summary>
    public void UpdateSuspended(string title, DonorDisplay donorDisplay, string? donorName, bool isActive, DateTimeOffset at, Guid? by)
    {
        EnsureType(OfferType.Suspended);
        ApplyCommon(title, CatalogItemId, donorDisplay, donorName);
        IsActive = isActive;
        MarkUpdated(at, by);
    }

    /// <summary>Verilən Bakı günü üçün qalan gündəlik vahid (lazy sıfırlama: başqa gündə tam kvota).</summary>
    public int AvailableToday(DateOnly bakuDate) => Type == OfferType.BusinessDaily
        ? Math.Max(0, DailyQuota - (QuotaDate == DateKey(bakuDate) ? UsedToday : 0))
        : Remaining;

    /// <summary>Gündəlik pəncərə bu Bakı anında açıqdırmı (pəncərə yoxdursa həmişə açıq).</summary>
    public bool IsWithinWindow(DateTimeOffset now)
    {
        if (AvailableFrom is null || AvailableTo is null)
        {
            return true;
        }

        var local = TimeOnly.FromDateTime(BakuTime.ToBakuTime(now).DateTime);
        var from = TimeOnly.ParseExact(AvailableFrom, "HH:mm", CultureInfo.InvariantCulture);
        var to = TimeOnly.ParseExact(AvailableTo, "HH:mm", CultureInfo.InvariantCulture);
        return from < to ? local >= from && local < to : local >= from || local < to;
    }

    private void ApplyDaily(DailyOfferDetails details)
    {
        if (!IsValidDailyQuota(details.DailyQuota))
        {
            throw new ArgumentOutOfRangeException(nameof(details), "Gündəlik kvota düzgün deyil.");
        }

        if (!IsValidWindow(details.AvailableFrom, details.AvailableTo))
        {
            throw new ArgumentException("Vaxt pəncərəsi düzgün deyil.", nameof(details));
        }

        ApplyCommon(details.Title, details.CatalogItemId, details.DonorDisplay, details.DonorName);
        DailyQuota = details.DailyQuota;
        AvailableFrom = details.AvailableFrom;
        AvailableTo = details.AvailableTo;
        IsActive = details.IsActive;
    }

    private void ApplyCommon(string title, CatalogItemId? catalogItemId, DonorDisplay donorDisplay, string? donorName)
    {
        Title = Guard.NotNullOrWhiteSpace(title).Trim();
        CatalogItemId = catalogItemId;
        DonorDisplay = Enum.IsDefined(donorDisplay) ? donorDisplay : throw new ArgumentOutOfRangeException(nameof(donorDisplay));
        DonorName = donorDisplay == DonorDisplay.Named && !string.IsNullOrWhiteSpace(donorName) ? donorName.Trim() : null;
    }

    private void EnsureType(OfferType expected)
    {
        if (Type != expected)
        {
            throw new InvalidOperationException($"Bu əməliyyat yalnız {expected} təklifləri üçündür.");
        }
    }
}

public sealed record DailyOfferDetails(
    string Title,
    CatalogItemId? CatalogItemId,
    int DailyQuota,
    string? AvailableFrom,
    string? AvailableTo,
    DonorDisplay DonorDisplay,
    string? DonorName,
    bool IsActive);

public sealed record SuspendedOfferDetails(
    string Title, CatalogItemId? CatalogItemId, int Quantity, DonorDisplay DonorDisplay, string? DonorName, Guid? DonorUserId);
