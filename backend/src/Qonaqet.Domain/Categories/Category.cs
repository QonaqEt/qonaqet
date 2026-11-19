using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Categories;

public readonly record struct CategoryId(Guid Value) : IStronglyTypedId<CategoryId>
{
    public static CategoryId From(Guid value) => new(value);
}

/// <summary>
/// Kateqoriyanın məxfilik səviyyəsi. <see cref="Sensitive"/> kateqoriyanın adı işçilərə, bizneslərə və lövhələrə heç vaxt
/// göstərilmir (orada "Qonaq" yazılır).
/// </summary>
public enum CategoryPrivacyLevel
{
    Public,
    Sensitive,
}

/// <summary>
/// Sosial qonaqlıq kateqoriyası (<c>categories</c>). Tamamilə datadır: admin kod dəyişmədən yenisini əlavə edir, kateqoriyaya
/// bağlı bütün davranış (təsdiq sahələri, limitlər, etibarlılıq) bu sənəddən oxunur. Kodda heç vaxt kateqoriya koduna görə
/// şərt yazılmır; istinad yalnız <see cref="CategoryId"/> ilədir.
/// </summary>
public sealed class Category : AggregateRoot<CategoryId>
{
    private Category(CategoryId id, string code, CategoryDetails details, DateTimeOffset createdAt, Guid? createdBy)
        : base(id, createdAt, createdBy)
    {
        Code = code;
        Apply(details);
    }

    private Category()
    {
    }

    /// <summary>Slug (<c>a-z</c>, rəqəm, tire). Kateqoriya istifadədə olandan sonra dəyişmir.</summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public Guid? IconFileId { get; private set; }

    /// <summary>Kateqoriya rəngi, <c>#RRGGBB</c> (data; UI komponentləri rəngi buradan oxuyur).</summary>
    public string Color { get; private set; } = string.Empty;

    public int SortOrder { get; private set; }

    /// <summary>Deaktiv kateqoriya siyahılarda görünmür, ona yeni təsdiq və claim yoxdur.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Açıq kateqoriya: təsdiq tələb olunmur, hər istifadəçi uyğundur.</summary>
    public bool IsOpen { get; private set; }

    public CategoryPrivacyLevel PrivacyLevel { get; private set; }

    public VerificationConfig Verification { get; private set; } = VerificationConfig.NotRequired;

    public CategoryLimits Limits { get; private set; } = CategoryLimits.None;

    public bool IsSensitive => PrivacyLevel == CategoryPrivacyLevel.Sensitive;

    public static Category Create(string code, CategoryDetails details, DateTimeOffset at, Guid? by)
    {
        ArgumentNullException.ThrowIfNull(details);
        return new Category(StronglyTypedId.New<CategoryId>(), NormalizeCode(code), details, at, by);
    }

    public void Update(CategoryDetails details, DateTimeOffset at, Guid? by)
    {
        ArgumentNullException.ThrowIfNull(details);
        Apply(details);
        MarkUpdated(at, by);
    }

    /// <summary>Kodu dəyişir. İstifadədə olub-olmadığını çağıran (handler) yoxlayır: bu, başqa kolleksiyalardan asılıdır.</summary>
    public bool ChangeCode(string code, DateTimeOffset at, Guid? by)
    {
        var normalized = NormalizeCode(code);
        if (normalized == Code)
        {
            return false;
        }

        Code = normalized;
        MarkUpdated(at, by);
        return true;
    }

    private static string NormalizeCode(string code) =>
        CategoryRules.IsValidCode(code) ? code : throw new ArgumentException("Kateqoriya kodu düzgün deyil.", nameof(code));

    private void Apply(CategoryDetails details)
    {
        Name = Guard.NotNullOrWhiteSpace(details.Name).Trim();
        Description = string.IsNullOrWhiteSpace(details.Description) ? null : details.Description.Trim();
        IconFileId = details.IconFileId == Guid.Empty ? null : details.IconFileId;
        Color = CategoryRules.IsValidColor(details.Color) ? details.Color.ToUpperInvariant() : throw new ArgumentException("Rəng düzgün deyil.", nameof(details));
        SortOrder = details.SortOrder;
        IsActive = details.IsActive;
        IsOpen = details.IsOpen;
        PrivacyLevel = details.PrivacyLevel;
        Verification = details.IsOpen ? VerificationConfig.NotRequired : Guard.NotNull(details.Verification);
        Limits = Guard.NotNull(details.Limits);
    }
}

/// <summary>Kateqoriyanın kodu istisna olmaqla redaktə olunan sahələri (dəyərlər əvvəlcədən validasiya olunub).</summary>
public sealed record CategoryDetails(
    string Name,
    string? Description,
    Guid? IconFileId,
    string Color,
    int SortOrder,
    bool IsActive,
    bool IsOpen,
    CategoryPrivacyLevel PrivacyLevel,
    VerificationConfig Verification,
    CategoryLimits Limits);

/// <summary>
/// Sosial qonaqlıq limitləri. İstifadəçinin bir neçə kateqoriyası varsa ən səxavətlisi tətbiq olunur (E06).
/// Ünvanlı hədiyyələrə heç bir kateqoriya limiti tətbiq olunmur.
/// </summary>
public sealed class CategoryLimits
{
    public const int MaxPerDay = 100;
    public const int MaxPerMonth = 3000;
    public const long MaxPoolSpendPerDayQepik = 1_000_000;

    private CategoryLimits(int perDay, int perMonth, bool poolAccess, long poolSpendPerDayQepik)
    {
        PerDay = perDay;
        PerMonth = perMonth;
        PoolAccess = poolAccess;
        PoolSpendPerDayQepik = poolSpendPerDayQepik;
    }

    private CategoryLimits()
    {
    }

    public static CategoryLimits None { get; } = new(0, 0, false, 0);

    public int PerDay { get; private set; }

    public int PerMonth { get; private set; }

    /// <summary>Hovuzdan claim edə bilər.</summary>
    public bool PoolAccess { get; private set; }

    /// <summary>Hovuzdan bir Bakı günündə xərclənə biləcək maksimum məbləğ (qəpik).</summary>
    public long PoolSpendPerDayQepik { get; private set; }

    public static bool IsValid(int perDay, int perMonth, bool poolAccess, long poolSpendPerDayQepik) =>
        perDay is >= 0 and <= MaxPerDay && perMonth >= perDay && perMonth <= MaxPerMonth
        && poolSpendPerDayQepik is >= 0 and <= MaxPoolSpendPerDayQepik && (poolAccess || poolSpendPerDayQepik == 0);

    public static CategoryLimits Create(int perDay, int perMonth, bool poolAccess, long poolSpendPerDayQepik) =>
        IsValid(perDay, perMonth, poolAccess, poolSpendPerDayQepik)
            ? new CategoryLimits(perDay, perMonth, poolAccess, poolSpendPerDayQepik)
            : throw new ArgumentOutOfRangeException(nameof(perDay), "Limitlər düzgün deyil.");
}
