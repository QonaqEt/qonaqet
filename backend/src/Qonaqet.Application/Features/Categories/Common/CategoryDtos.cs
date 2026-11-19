using Qonaqet.Domain.Categories;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Categories.Common;

public sealed record CategorySubtypeDto(string Code, string Name);

/// <summary>Dinamik sahə. <see cref="Options"/> yalnız <see cref="VerificationFieldType.Select"/> üçündür.</summary>
public sealed record VerificationFieldDto(string Key, string Label, VerificationFieldType Type, bool Required, IReadOnlyList<string>? Options = null);

public sealed record CategoryValidityDto(ValidityType Type, int? Month = null, int? Day = null, int? Months = null);

public sealed record VerificationConfigDto(
    bool RequiresFin,
    int MinFiles,
    int MaxFiles,
    string? DocumentHint,
    IReadOnlyList<CategorySubtypeDto>? Subtypes,
    IReadOnlyList<VerificationFieldDto>? Fields,
    CategoryValidityDto? Validity);

public sealed record CategoryLimitsDto(int PerDay, int PerMonth, bool PoolAccess, long PoolSpendPerDayQepik);

/// <summary>
/// Kateqoriya (ictimai <c>GET categories</c> və admin). Bütün kateqoriyaya bağlı UI (sihirbaz, filtrlər, nişanlar) yalnız bundan
/// qurulur: kateqoriyaya xüsusi ekran yoxdur.
/// </summary>
public sealed record CategoryDto(
    CategoryId Id,
    string Code,
    string Name,
    string? Description,
    Guid? IconFileId,
    string Color,
    int SortOrder,
    bool IsActive,
    bool IsOpen,
    CategoryPrivacyLevel PrivacyLevel,
    VerificationConfigDto Verification,
    CategoryLimitsDto Limits,
    bool IsDeleted);

public static class CategoryMapping
{
    public static CategoryDto ToDto(this Category category)
    {
        ArgumentNullException.ThrowIfNull(category);
        return new CategoryDto(
            category.Id, category.Code, category.Name, category.Description, category.IconFileId, category.Color, category.SortOrder,
            category.IsActive, category.IsOpen, category.PrivacyLevel, category.Verification.ToDto(), category.Limits.ToDto(), category.IsDeleted);
    }

    public static VerificationConfigDto ToDto(this VerificationConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new VerificationConfigDto(
            config.RequiresFin, config.MinFiles, config.MaxFiles, config.DocumentHint,
            [.. config.Subtypes.Select(s => new CategorySubtypeDto(s.Code, s.Name))],
            [.. config.Fields.Select(f => new VerificationFieldDto(f.Key, f.Label, f.Type, f.Required, f.Type == VerificationFieldType.Select ? f.Options : null))],
            new CategoryValidityDto(config.Validity.Type, config.Validity.Month, config.Validity.Day, config.Validity.Months));
    }

    public static CategoryLimitsDto ToDto(this CategoryLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        return new CategoryLimitsDto(limits.PerDay, limits.PerMonth, limits.PoolAccess, limits.PoolSpendPerDayQepik);
    }
}

/// <summary>Yaratma və yeniləmənin ortaq sahələri.</summary>
public interface ICategoryInput
{
    string Code { get; }

    string Name { get; }

    string? Description { get; }

    Guid? IconFileId { get; }

    string Color { get; }

    int SortOrder { get; }

    bool IsActive { get; }

    bool IsOpen { get; }

    CategoryPrivacyLevel PrivacyLevel { get; }

    /// <summary>Açıq kateqoriyada nəzərə alınmır.</summary>
    VerificationConfigDto? Verification { get; }

    CategoryLimitsDto Limits { get; }
}

public static class CategoryInput
{
    /// <summary>Validasiyadan keçmiş girişi domen dəyərlərinə çevirir.</summary>
    public static CategoryDetails ToDetails(this ICategoryInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var verification = input.IsOpen || input.Verification is null ? VerificationConfig.NotRequired : ToConfig(input.Verification);
        var limits = CategoryLimits.Create(input.Limits.PerDay, input.Limits.PerMonth, input.Limits.PoolAccess, input.Limits.PoolSpendPerDayQepik);
        return new CategoryDetails(
            input.Name, input.Description, input.IconFileId, input.Color, input.SortOrder, input.IsActive, input.IsOpen, input.PrivacyLevel, verification, limits);
    }

    public static VerificationConfig ToConfig(VerificationConfigDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var validity = dto.Validity ?? new CategoryValidityDto(ValidityType.None);
        return VerificationConfig.Create(
            dto.RequiresFin, dto.MinFiles, dto.MaxFiles, dto.DocumentHint,
            [.. (dto.Subtypes ?? []).Select(s => CategorySubtype.Create(s.Code, s.Name))],
            [.. (dto.Fields ?? []).Select(f => VerificationField.Create(f.Key, f.Label, f.Type, f.Required, f.Options))],
            CategoryValidity.Create(validity.Type, validity.Month, validity.Day, validity.Months));
    }
}

/// <summary>
/// Kateqoriya sahələrinin qaydaları. Validator interfeys üzərindən işlədiyi üçün sahə qaydaları <c>Rule("field", …)</c> ilə yazılır.
/// </summary>
public abstract class CategoryValidator<T> : Validator<T>
    where T : ICategoryInput
{
    protected CategoryValidator()
    {
        Rule("code", x => CategoryRules.IsValidCode(x.Code), "category_code_invalid",
            $"Kod kiçik latın hərfləri, rəqəm və tiredən ibarət olmalıdır (ən çox {CategoryRules.CodeMaxLength} simvol).");
        Rule("name", x => !string.IsNullOrWhiteSpace(x.Name) && x.Name.Length <= CategoryRules.NameMaxLength, ValidationCodes.Invalid,
            $"Ad boş ola bilməz, ən çox {CategoryRules.NameMaxLength} simvol.");
        Rule("description", x => x.Description is null || x.Description.Length <= CategoryRules.DescriptionMaxLength, ValidationCodes.TooLong,
            $"Təsvir ən çox {CategoryRules.DescriptionMaxLength} simvol ola bilər.");
        Rule("color", x => CategoryRules.IsValidColor(x.Color), "color_invalid", "Rəng #RRGGBB formatında olmalıdır.");
        Rule("sortOrder", x => x.SortOrder is >= 0 and <= 10_000, ValidationCodes.OutOfRange, "Sıra 0 ilə 10 000 arasında olmalıdır.");
        Rule("privacyLevel", x => Enum.IsDefined(x.PrivacyLevel), ValidationCodes.Invalid, "Məxfilik səviyyəsi düzgün deyil.");
        Rule("iconFileId", x => x.IconFileId != Guid.Empty, "category_icon_invalid", "İkon faylı düzgün deyil.");
        Rule("limits", x => x.Limits is { } l && CategoryLimits.IsValid(l.PerDay, l.PerMonth, l.PoolAccess, l.PoolSpendPerDayQepik), "limits_invalid",
            $"Limitlər: gündə 0–{CategoryLimits.MaxPerDay}, ayda gündəlikdən az olmayaraq ən çox {CategoryLimits.MaxPerMonth}; hovuz xərci yalnız hovuza giriş olduqda.");

        Rule("verification", x => x.IsOpen || x.Verification is not null, ValidationCodes.Required, "Təsdiq tələb edən kateqoriya üçün təsdiq konfiqurasiyası məcburidir.");
        Rule("verification.files", x => Closed(x) is not { } v || (v.MinFiles >= 0 && v.MaxFiles >= v.MinFiles && v.MaxFiles <= VerificationConfig.MaxFilesLimit),
            "files_range_invalid", $"Fayl sayı: 0 ≤ minimum ≤ maksimum ≤ {VerificationConfig.MaxFilesLimit}.");
        Rule("verification.documentHint", x => Closed(x)?.DocumentHint is not { } hint || hint.Length <= VerificationConfig.DocumentHintMaxLength,
            ValidationCodes.TooLong, $"Sənəd izahı ən çox {VerificationConfig.DocumentHintMaxLength} simvol ola bilər.");
        Rule("verification.subtypes", x => Closed(x) is not { } v || AreValidSubtypes(v.Subtypes ?? []), "subtypes_invalid",
            $"Alt növlər: ən çox {VerificationConfig.MaxSubtypes}, kodlar unikal slug, adlar boş olmayan (≤{CategorySubtype.NameMaxLength}).");
        Rule("verification.fields", x => Closed(x) is not { } v || AreValidFields(v.Fields ?? []), "fields_invalid",
            $"Sahələr: ən çox {VerificationConfig.MaxFields}, açarlar unikal (kiçik hərflə başlayan hərf/rəqəm), etiketlər boş olmayan; seçim sahəsində 1–{VerificationField.MaxOptions} unikal variant.");
        Rule("verification.validity", x => Closed(x) is not { } v || v.Validity is null || CategoryValidity.IsValid(v.Validity.Type, v.Validity.Month, v.Validity.Day, v.Validity.Months),
            "validity_invalid", "Etibarlılıq: None; FixedDate üçün ay və gün; Months üçün 1–120 ay.");
    }

    private static VerificationConfigDto? Closed(T input) => input.IsOpen ? null : input.Verification;

    private static bool AreValidSubtypes(IReadOnlyList<CategorySubtypeDto> subtypes) =>
        subtypes.Count <= VerificationConfig.MaxSubtypes
        && subtypes.All(s => s is not null && CategoryRules.IsValidCode(s.Code) && !string.IsNullOrWhiteSpace(s.Name) && s.Name.Length <= CategorySubtype.NameMaxLength)
        && subtypes.Select(s => s.Code).Distinct(StringComparer.Ordinal).Count() == subtypes.Count;

    private static bool AreValidFields(IReadOnlyList<VerificationFieldDto> fields) =>
        fields.Count <= VerificationConfig.MaxFields
        && fields.All(IsValidField)
        && fields.Select(f => f.Key).Distinct(StringComparer.Ordinal).Count() == fields.Count;

    private static bool IsValidField(VerificationFieldDto? field)
    {
        if (field is null || !CategoryRules.IsValidFieldKey(field.Key) || string.IsNullOrWhiteSpace(field.Label)
            || field.Label.Length > VerificationField.LabelMaxLength || !Enum.IsDefined(field.Type))
        {
            return false;
        }

        if (field.Type != VerificationFieldType.Select)
        {
            return true;
        }

        var options = (field.Options ?? []).Select(o => o?.Trim() ?? string.Empty).ToList();
        return options.Count is > 0 and <= VerificationField.MaxOptions
            && options.All(o => o.Length is > 0 and <= VerificationField.OptionMaxLength)
            && options.Distinct(StringComparer.Ordinal).Count() == options.Count;
    }
}
