using System.Globalization;
using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Categories;

/// <summary>Təsdiq sihirbazının dinamik sahə növü.</summary>
public enum VerificationFieldType
{
    Text,
    Select,
    Date,
}

/// <summary>Təsdiqin etibarlılıq qaydası.</summary>
public enum ValidityType
{
    /// <summary>Müddətsiz.</summary>
    None,

    /// <summary>Hər il eyni tarixdə bitir (məs. tələbələr üçün 30 sentyabr).</summary>
    FixedDate,

    /// <summary>Təsdiqdən N ay sonra bitir.</summary>
    Months,
}

/// <summary>
/// Kateqoriyanın təsdiq konfiqurasiyası: mobil və veb sihirbazı, server isə yoxlamanı yalnız bundan qurur.
/// Açıq kateqoriyalarda <see cref="NotRequired"/>-dir.
/// </summary>
public sealed class VerificationConfig
{
    public const int MaxFilesLimit = 10;
    public const int MaxSubtypes = 20;
    public const int MaxFields = 20;
    public const int DocumentHintMaxLength = 500;

    private VerificationConfig(
        bool requiresFin, int minFiles, int maxFiles, string? documentHint,
        IReadOnlyList<CategorySubtype> subtypes, IReadOnlyList<VerificationField> fields, CategoryValidity validity)
    {
        RequiresFin = requiresFin;
        MinFiles = minFiles;
        MaxFiles = maxFiles;
        DocumentHint = documentHint;
        Subtypes = subtypes;
        Fields = fields;
        Validity = validity;
    }

    private VerificationConfig()
    {
    }

    public static VerificationConfig NotRequired { get; } = new(false, 0, 0, null, [], [], CategoryValidity.None);

    /// <summary>FIN (şəxsiyyət vəsiqəsinin fərdi identifikasiya nömrəsi) tələb olunur; yalnız HMAC hash-i saxlanılır.</summary>
    public bool RequiresFin { get; private set; }

    public int MinFiles { get; private set; }

    public int MaxFiles { get; private set; }

    /// <summary>İstifadəçiyə hansı sənədin lazım olduğunu izah edən mətn.</summary>
    public string? DocumentHint { get; private set; }

    /// <summary>Alt növlər (məs. qazi / şəhid ailəsi). Boşdursa alt növ seçilmir.</summary>
    public IReadOnlyList<CategorySubtype> Subtypes { get; private set; } = [];

    public IReadOnlyList<VerificationField> Fields { get; private set; } = [];

    public CategoryValidity Validity { get; private set; } = CategoryValidity.None;

    public static VerificationConfig Create(
        bool requiresFin, int minFiles, int maxFiles, string? documentHint,
        IReadOnlyList<CategorySubtype> subtypes, IReadOnlyList<VerificationField> fields, CategoryValidity validity)
    {
        ArgumentNullException.ThrowIfNull(subtypes);
        ArgumentNullException.ThrowIfNull(fields);
        if (minFiles < 0 || maxFiles < minFiles || maxFiles > MaxFilesLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(minFiles), "Fayl sayı düzgün deyil.");
        }

        if (subtypes.Count > MaxSubtypes || subtypes.Select(s => s.Code).Distinct(StringComparer.Ordinal).Count() != subtypes.Count)
        {
            throw new ArgumentException("Alt növlərin kodları təkrarlanmamalıdır.", nameof(subtypes));
        }

        if (fields.Count > MaxFields || fields.Select(f => f.Key).Distinct(StringComparer.Ordinal).Count() != fields.Count)
        {
            throw new ArgumentException("Sahə açarları təkrarlanmamalıdır.", nameof(fields));
        }

        return new VerificationConfig(
            requiresFin, minFiles, maxFiles, string.IsNullOrWhiteSpace(documentHint) ? null : documentHint.Trim(),
            [.. subtypes], [.. fields], Guard.NotNull(validity));
    }

    public bool HasSubtype(string? code) => code is not null && Subtypes.Any(s => s.Code == code);
}

/// <summary>Kateqoriyanın alt növü.</summary>
public sealed class CategorySubtype
{
    public const int NameMaxLength = 80;

    private CategorySubtype(string code, string name)
    {
        Code = code;
        Name = name;
    }

    private CategorySubtype()
    {
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public static CategorySubtype Create(string code, string name) =>
        CategoryRules.IsValidCode(code)
            ? new CategorySubtype(code, Guard.NotNullOrWhiteSpace(name).Trim())
            : throw new ArgumentException("Alt növ kodu düzgün deyil.", nameof(code));
}

/// <summary>Sihirbazın dinamik sahəsi. <see cref="VerificationFieldType.Select"/> sahəsinin seçimləri <see cref="Options"/>-dadır.</summary>
public sealed class VerificationField
{
    public const int LabelMaxLength = 80;
    public const int MaxOptions = 500;
    public const int OptionMaxLength = 150;

    /// <summary>Mətn cavabının maksimum uzunluğu.</summary>
    public const int TextValueMaxLength = 200;

    private VerificationField(string key, string label, VerificationFieldType type, bool required, IReadOnlyList<string> options)
    {
        Key = key;
        Label = label;
        Type = type;
        Required = required;
        Options = options;
    }

    private VerificationField()
    {
    }

    /// <summary>Sahə açarı (<c>camelCase</c>, hərf və rəqəm) — cavablar bu açarla saxlanılır.</summary>
    public string Key { get; private set; } = string.Empty;

    public string Label { get; private set; } = string.Empty;

    public VerificationFieldType Type { get; private set; }

    public bool Required { get; private set; }

    public IReadOnlyList<string> Options { get; private set; } = [];

    public static VerificationField Create(string key, string label, VerificationFieldType type, bool required, IReadOnlyList<string>? options)
    {
        if (!CategoryRules.IsValidFieldKey(key))
        {
            throw new ArgumentException("Sahə açarı düzgün deyil.", nameof(key));
        }

        var list = type == VerificationFieldType.Select ? [.. (options ?? []).Select(o => o.Trim())] : new List<string>();
        if (type == VerificationFieldType.Select
            && (list.Count == 0 || list.Count > MaxOptions || list.Any(o => o.Length is 0 or > OptionMaxLength) || list.Distinct(StringComparer.Ordinal).Count() != list.Count))
        {
            throw new ArgumentException("Seçim sahəsinin variantları düzgün deyil.", nameof(options));
        }

        return new VerificationField(key, Guard.NotNullOrWhiteSpace(label).Trim(), type, required, list);
    }

    /// <summary>Cavab bu sahənin növünə uyğundurmu (boş cavabı tələb yoxlaması ayrıca həll edir).</summary>
    public bool Accepts(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Type switch
        {
            VerificationFieldType.Text => value.Length <= TextValueMaxLength,
            VerificationFieldType.Select => Options.Contains(value, StringComparer.Ordinal),
            VerificationFieldType.Date => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            _ => false,
        };
    }
}

/// <summary>Etibarlılıq qaydası: <see cref="ValidityType.FixedDate"/> üçün ay/gün, <see cref="ValidityType.Months"/> üçün ay sayı.</summary>
public sealed class CategoryValidity
{
    public const int MaxMonths = 120;

    private CategoryValidity(ValidityType type, int? month, int? day, int? months)
    {
        Type = type;
        Month = month;
        Day = day;
        Months = months;
    }

    private CategoryValidity()
    {
    }

    public static CategoryValidity None { get; } = new(ValidityType.None, null, null, null);

    public ValidityType Type { get; private set; }

    public int? Month { get; private set; }

    public int? Day { get; private set; }

    public int? Months { get; private set; }

    public static bool IsValid(ValidityType type, int? month, int? day, int? months) => type switch
    {
        ValidityType.None => month is null && day is null && months is null,

        // 29 fevral qəbul olunmur: hər il mövcud olan tarix olmalıdır.
        ValidityType.FixedDate => month is >= 1 and <= 12 && day is >= 1 && day <= DateTime.DaysInMonth(2023, month.Value) && months is null,
        ValidityType.Months => months is >= 1 and <= MaxMonths && month is null && day is null,
        _ => false,
    };

    public static CategoryValidity Create(ValidityType type, int? month, int? day, int? months) =>
        IsValid(type, month, day, months) ? new CategoryValidity(type, month, day, months) : throw new ArgumentException("Etibarlılıq qaydası düzgün deyil.", nameof(type));

    /// <summary>
    /// Təsdiq anından etibarlılığın (eksklüziv) sonu, Bakı təqvimi ilə; <see cref="ValidityType.None"/> üçün <c>null</c>.
    /// <list type="bullet">
    /// <item><see cref="ValidityType.Months"/>: təsdiq gününə N ay əlavə olunur, həmin günün sonuna qədər etibarlıdır.</item>
    /// <item><see cref="ValidityType.FixedDate"/>: həmin ay/günün ən yaxın gələcək tarixi (bu gün də daxil), o günün sonuna qədər.</item>
    /// </list>
    /// </summary>
    public DateTimeOffset? ValidUntil(DateTimeOffset approvedAt)
    {
        var today = BakuTime.ToBakuDate(approvedAt);
        switch (Type)
        {
            case ValidityType.Months:
                return BakuTime.EndOfDayUtc(today.AddMonths(Months!.Value));
            case ValidityType.FixedDate:
                var candidate = new DateOnly(today.Year, Month!.Value, Day!.Value);
                return BakuTime.EndOfDayUtc(candidate < today ? candidate.AddYears(1) : candidate);
            default:
                return null;
        }
    }

    public static CategoryValidity FixedDate(int month, int day) => Create(ValidityType.FixedDate, month, day, null);

    public static CategoryValidity ForMonths(int months) => Create(ValidityType.Months, null, null, months);
}
