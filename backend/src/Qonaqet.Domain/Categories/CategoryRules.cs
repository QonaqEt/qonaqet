namespace Qonaqet.Domain.Categories;

/// <summary>Kateqoriya sahələrinin formatı: kod (slug), rəng və dinamik sahə açarı.</summary>
public static class CategoryRules
{
    public const int CodeMaxLength = 40;
    public const int NameMaxLength = 80;
    public const int DescriptionMaxLength = 1000;
    public const int FieldKeyMaxLength = 40;

    /// <summary>Kiçik latın hərfləri və rəqəmlər, tire ilə ayrılmış hissələr: <c>veteran-family</c>.</summary>
    public static bool IsValidCode(string? code)
    {
        if (code is null || code.Length is 0 or > CodeMaxLength || code[0] == '-' || code[^1] == '-' || code.Contains("--", StringComparison.Ordinal))
        {
            return false;
        }

        return code.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');
    }

    /// <summary><c>#RRGGBB</c>.</summary>
    public static bool IsValidColor(string? color) =>
        color is { Length: 7 } && color[0] == '#' && color[1..].All(char.IsAsciiHexDigit);

    /// <summary>Sahə açarı: kiçik hərflə başlayır, sonra hərf və rəqəm (<c>university</c>, <c>studentId</c>). Mongo açarı kimi təhlükəsizdir.</summary>
    public static bool IsValidFieldKey(string? key) =>
        key is { Length: > 0 and <= FieldKeyMaxLength } && char.IsAsciiLetterLower(key[0]) && key.All(char.IsAsciiLetterOrDigit);
}
