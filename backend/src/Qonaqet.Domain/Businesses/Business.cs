using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Businesses;

public readonly record struct BusinessId(Guid Value) : IStronglyTypedId<BusinessId>
{
    public static BusinessId From(Guid value) => new(value);
}

/// <summary>Biznes növü (<c>businesses.type</c>). Platforma ümumidir: yemək-içkidən kənar növlər də var.</summary>
public enum BusinessType
{
    Cafe,
    Restaurant,
    Bakery,
    Confectionery,
    Market,
    Pharmacy,
    Bookstore,
    Florist,
    Barber,
    Other,
}

/// <summary>Biznesin vəziyyəti: müraciət <see cref="Pending"/>, admin aktivləşdirəndə <see cref="Active"/>, dayandırılanda <see cref="Suspended"/>.</summary>
public enum BusinessStatus
{
    Pending,
    Active,
    Suspended,
}

/// <summary>
/// Biznes (<c>businesses</c>). Müraciətlə <see cref="BusinessStatus.Pending"/> yaranır; admin aktivləşdirəndə müraciətçi
/// sahib olur. Kataloq ayrıca aqreqatdır (<c>catalogItems</c>), işçilər <c>businessMemberships</c>-dədir.
/// </summary>
public sealed class Business : AggregateRoot<BusinessId>
{
    public const int NameMaxLength = 120;
    public const int AddressMaxLength = 300;

    /// <summary>Default voucher həddi: 2–200 AZN (şəbəkə voucher-i ilə eyni).</summary>
    public const long DefaultVoucherMinQepik = 200;
    public const long DefaultVoucherMaxQepik = 20_000;

    /// <summary>Biznesin özü üçün təyin edə biləcəyi voucher həddləri: 1–1000 AZN.</summary>
    public const long VoucherLimitFloorQepik = 100;
    public const long VoucherLimitCeilingQepik = 100_000;

    private List<DailyHours> _workingHours = [];

    private Business(BusinessId id, BusinessProfile profile, string slug, Guid ownerUserId, DateTimeOffset createdAt)
        : base(id, createdAt, ownerUserId)
    {
        Slug = slug;
        OwnerUserId = ownerUserId;
        Status = BusinessStatus.Pending;
        Apply(profile);
    }

    private Business()
    {
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>URL üçün unikal ad (<c>/b/{slug}</c>); müraciətdə addan yaranır və dəyişmir.</summary>
    public string Slug { get; private set; } = string.Empty;

    public BusinessType Type { get; private set; }

    /// <summary>VÖEN: 10 rəqəm.</summary>
    public string Voen { get; private set; } = string.Empty;

    public string Address { get; private set; } = string.Empty;

    public GeoPoint Location { get; private set; } = null!;

    public string Phone { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public Guid? LogoFileId { get; private set; }

    public Guid? CoverFileId { get; private set; }

    /// <summary>Ödəniş (payout) hesabı. Yalnız sahib və admin görür.</summary>
    public string Iban { get; private set; } = string.Empty;

    public IReadOnlyList<DailyHours> WorkingHours
    {
        get => _workingHours;
        private set => _workingHours = [.. value];
    }

    public bool AcceptsPool { get; private set; }

    public bool AcceptsNetworkVouchers { get; private set; }

    public long VoucherMinQepik { get; private set; } = DefaultVoucherMinQepik;

    public long VoucherMaxQepik { get; private set; } = DefaultVoucherMaxQepik;

    public BusinessStatus Status { get; private set; }

    /// <summary>Müraciət edən istifadəçi; aktivləşdirmədə sahib olur.</summary>
    public Guid OwnerUserId { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    public DateTimeOffset? SuspendedAt { get; private set; }

    public string? SuspendReason { get; private set; }

    public bool IsActive => Status == BusinessStatus.Active;

    public static Business Submit(BusinessProfile profile, string slug, Guid applicantUserId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var business = new Business(StronglyTypedId.New<BusinessId>(), profile, Guard.NotNullOrWhiteSpace(slug), Guard.NotEmpty(applicantUserId), at);
        business.Raise(new BusinessApplied(business.Id, applicantUserId, at));
        return business;
    }

    /// <summary>Profil sahələrini (ad, ünvan, xəritə nöqtəsi, əlaqə, IBAN, iş saatları, şəkillər) yeniləyir. Slug dəyişmir.</summary>
    public void UpdateProfile(BusinessProfile profile, DateTimeOffset at, Guid? by)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Apply(profile);
        MarkUpdated(at, by);
    }

    /// <summary><see cref="BusinessStatus.Pending"/> və ya <see cref="BusinessStatus.Suspended"/> → <see cref="BusinessStatus.Active"/>.</summary>
    public bool Activate(DateTimeOffset at, Guid? by)
    {
        if (Status == BusinessStatus.Active)
        {
            return false;
        }

        Status = BusinessStatus.Active;
        ActivatedAt ??= Guard.Utc(at);
        SuspendedAt = null;
        SuspendReason = null;
        MarkUpdated(at, by);
        Raise(new BusinessActivated(Id, OwnerUserId, at));
        return true;
    }

    public bool Suspend(string reason, DateTimeOffset at, Guid? by)
    {
        if (Status == BusinessStatus.Suspended)
        {
            return false;
        }

        Status = BusinessStatus.Suspended;
        SuspendedAt = Guard.Utc(at);
        SuspendReason = Guard.NotNullOrWhiteSpace(reason).Trim();
        MarkUpdated(at, by);
        Raise(new BusinessSuspended(Id, at));
        return true;
    }

    /// <summary>Hədiyyə/hovuz parametrləri. <paramref name="voucherMinQepik"/> ≤ <paramref name="voucherMaxQepik"/> olmalıdır.</summary>
    public void ChangeSettings(bool acceptsPool, bool acceptsNetworkVouchers, long voucherMinQepik, long voucherMaxQepik, DateTimeOffset at, Guid? by)
    {
        if (voucherMinQepik < VoucherLimitFloorQepik || voucherMaxQepik > VoucherLimitCeilingQepik || voucherMinQepik > voucherMaxQepik)
        {
            throw new ArgumentOutOfRangeException(nameof(voucherMinQepik), "Voucher həddləri düzgün deyil.");
        }

        AcceptsPool = acceptsPool;
        AcceptsNetworkVouchers = acceptsNetworkVouchers;
        VoucherMinQepik = voucherMinQepik;
        VoucherMaxQepik = voucherMaxQepik;
        MarkUpdated(at, by);
    }

    private void Apply(BusinessProfile profile)
    {
        Name = Guard.NotNullOrWhiteSpace(profile.Name).Trim();
        Type = profile.Type;
        Voen = BusinessRules.IsValidVoen(profile.Voen) ? profile.Voen : throw new ArgumentException("VÖEN düzgün deyil.", nameof(profile));
        Address = Guard.NotNullOrWhiteSpace(profile.Address).Trim();
        Location = Guard.NotNull(profile.Location);
        Phone = BusinessRules.NormalizePhone(profile.Phone) ?? throw new ArgumentException("Telefon düzgün deyil.", nameof(profile));
        Email = Guard.NotNullOrWhiteSpace(profile.Email).Trim().ToLowerInvariant();
        Iban = BusinessRules.NormalizeIban(profile.Iban) ?? throw new ArgumentException("IBAN düzgün deyil.", nameof(profile));
        _workingHours = [.. profile.WorkingHours.OrderBy(h => h.Day)];
        LogoFileId = profile.LogoFileId;
        CoverFileId = profile.CoverFileId;
    }
}

/// <summary>Müraciət və profil yeniləməsində verilən sahələr (dəyərlər əvvəlcədən validasiya olunub).</summary>
public sealed record BusinessProfile(
    string Name,
    BusinessType Type,
    string Voen,
    string Address,
    GeoPoint Location,
    string Phone,
    string Email,
    string Iban,
    IReadOnlyList<DailyHours> WorkingHours,
    Guid? LogoFileId,
    Guid? CoverFileId);

/// <summary>GeoJSON nöqtəsi (<c>{ type: "Point", coordinates: [lng, lat] }</c>) — 2dsphere indeksi üçün.</summary>
public sealed class GeoPoint
{
    private GeoPoint(double latitude, double longitude)
    {
        Coordinates = [longitude, latitude];
    }

    private GeoPoint()
    {
    }

    public string Type { get; private set; } = "Point";

    /// <summary>GeoJSON qaydası: əvvəl uzunluq, sonra en.</summary>
    public IReadOnlyList<double> Coordinates { get; private set; } = [];

    public double Latitude => Coordinates[1];

    public double Longitude => Coordinates[0];

    public static bool IsValid(double latitude, double longitude) =>
        double.IsFinite(latitude) && double.IsFinite(longitude) && latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;

    public static GeoPoint At(double latitude, double longitude) =>
        IsValid(latitude, longitude) ? new GeoPoint(latitude, longitude) : throw new ArgumentOutOfRangeException(nameof(latitude), "Koordinatlar düzgün deyil.");
}

/// <summary>Bir günün iş saatları (<c>HH:mm</c>, Bakı vaxtı). <see cref="Closes"/> &lt; <see cref="Opens"/> gecəyarısını keçən növbədir.</summary>
public sealed class DailyHours
{
    private DailyHours(DayOfWeek day, string opens, string closes)
    {
        Day = day;
        Opens = opens;
        Closes = closes;
    }

    private DailyHours()
    {
    }

    public DayOfWeek Day { get; private set; }

    public string Opens { get; private set; } = string.Empty;

    public string Closes { get; private set; } = string.Empty;

    public static DailyHours Create(DayOfWeek day, string opens, string closes) =>
        IsValid(day, opens, closes) ? new DailyHours(day, opens, closes) : throw new ArgumentException("İş saatları düzgün deyil.", nameof(opens));

    public static bool IsValid(DayOfWeek day, string? opens, string? closes) =>
        Enum.IsDefined(day) && BusinessRules.IsClockTime(opens) && BusinessRules.IsClockTime(closes) && opens != closes;
}

/// <summary>Müraciət göndərildi (admin növbəsi üçün).</summary>
public sealed record BusinessApplied(BusinessId BusinessId, Guid ApplicantUserId, DateTimeOffset At) : DomainEvent(At);

/// <summary>Biznes aktivləşdi: müraciətçi <c>BusinessOwner</c> olur.</summary>
public sealed record BusinessActivated(BusinessId BusinessId, Guid OwnerUserId, DateTimeOffset At) : DomainEvent(At);

/// <summary>
/// Biznes dayandırıldı. Consumer-lər: biznesə bağlı hədiyyələr admin təsdiqi ilə qaytarılır/bağışlanır (E09),
/// lövhələrdən gizlənir (E11).
/// </summary>
public sealed record BusinessSuspended(BusinessId BusinessId, DateTimeOffset At) : DomainEvent(At);

/// <summary>
/// Biznes (admin tərəfindən) soft delete olundu. Kataloq və üzvlüklər eyni tranzaksiyada kaskad silinir; təkliflər və aktiv
/// claim-lər bu hadisənin consumer-indədir (E06-T09).
/// </summary>
public sealed record BusinessDeleted(BusinessId BusinessId, DateTimeOffset At) : DomainEvent(At);

/// <summary>Silinmiş biznes bərpa olundu; kaskadla silinənlər də geri qaytarılır.</summary>
public sealed record BusinessRestored(BusinessId BusinessId, DateTimeOffset At) : DomainEvent(At);

/// <summary>Kaskad soft delete-in sabit səbəbi: bərpa yalnız bu səbəblə silinənləri geri qaytarır.</summary>
public static class BusinessCascade
{
    public const string DeleteReason = "business_deleted";
}
