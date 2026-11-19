using Qonaqet.Domain.Categories;

namespace Qonaqet.Domain.Verification;

/// <summary>Təsdiq cavabının bir qüsuru: sahə yolu (<c>fields.university</c>, <c>subtype</c>, <c>fin</c>, <c>files</c>) və sabit kod.</summary>
public sealed record VerificationProblem(string Field, string Code, string Message);

/// <summary>
/// Təsdiq sorğusunun yoxlanması — tamamilə kateqoriya sənədindən qurulur (alt növ, tələb olunan sahələr, sahə növləri, FIN,
/// fayl sayı). Heç bir kateqoriya koduna bağlı qayda yoxdur.
/// </summary>
public static class VerificationRules
{
    public const int FinLength = 7;

    /// <summary>FIN: 7 latın hərfi və rəqəmdən ibarət, böyük hərfə çevrilir; düzgün deyilsə <c>null</c>.</summary>
    public static string? NormalizeFin(string? fin)
    {
        var value = fin?.Trim().ToUpperInvariant();
        return value is { Length: FinLength } && value.All(c => char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c)) ? value : null;
    }

    public static IReadOnlyList<VerificationProblem> Check(
        VerificationConfig config, string? subtype, IReadOnlyDictionary<string, string> fields, string? fin, int fileCount)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(fields);
        var problems = new List<VerificationProblem>();

        if (config.Subtypes.Count > 0 && !config.HasSubtype(subtype))
        {
            problems.Add(new("subtype", "subtype_invalid", "Alt növü siyahıdan seçin."));
        }
        else if (config.Subtypes.Count == 0 && !string.IsNullOrWhiteSpace(subtype))
        {
            problems.Add(new("subtype", "subtype_not_expected", "Bu kateqoriyanın alt növü yoxdur."));
        }

        foreach (var key in fields.Keys.Where(k => config.Fields.All(f => f.Key != k)))
        {
            problems.Add(new($"fields.{key}", "field_unknown", "Bu sahə kateqoriyada yoxdur."));
        }

        foreach (var field in config.Fields)
        {
            var hasValue = fields.TryGetValue(field.Key, out var value) && !string.IsNullOrWhiteSpace(value);
            if (!hasValue)
            {
                if (field.Required)
                {
                    problems.Add(new($"fields.{field.Key}", "field_required", $"\"{field.Label}\" sahəsi məcburidir."));
                }
            }
            else if (!field.Accepts(value!.Trim()))
            {
                problems.Add(new($"fields.{field.Key}", "field_invalid", $"\"{field.Label}\" sahəsinin dəyəri düzgün deyil."));
            }
        }

        if (config.RequiresFin && NormalizeFin(fin) is null)
        {
            problems.Add(new("fin", "fin_invalid", "FIN 7 simvoldan (latın hərfləri və rəqəmlər) ibarət olmalıdır."));
        }
        else if (!config.RequiresFin && !string.IsNullOrEmpty(fin))
        {
            problems.Add(new("fin", "fin_not_expected", "Bu kateqoriya FIN tələb etmir."));
        }

        if (fileCount < config.MinFiles || fileCount > config.MaxFiles)
        {
            problems.Add(new("files", "files_count_invalid", $"{config.MinFiles}–{config.MaxFiles} sənəd yükləyin."));
        }

        return problems;
    }
}
