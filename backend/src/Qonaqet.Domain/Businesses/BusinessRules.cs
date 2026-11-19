using System.Globalization;
using System.Text;

namespace Qonaqet.Domain.Businesses;

/// <summary>Biznes sahələrinin formatı: VÖEN, Azərbaycan IBAN-ı, telefon, iş saatı və slug.</summary>
public static class BusinessRules
{
    public const int SlugMaxLength = 60;

    /// <summary>VÖEN: düz 10 rəqəm.</summary>
    public static bool IsValidVoen(string? voen) => voen is { Length: 10 } && voen.All(char.IsAsciiDigit);

    /// <summary>
    /// Azərbaycan IBAN-ı: <c>AZ</c> + 2 yoxlama rəqəmi + 4 hərfli bank kodu + 20 simvol (cəmi 28), ISO 13616 mod-97 yoxlaması ilə.
    /// Boşluqlar atılır, hərflər böyüdülür; düzgün deyilsə <c>null</c>.
    /// </summary>
    public static string? NormalizeIban(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban))
        {
            return null;
        }

        var value = new string([.. iban.Where(c => !char.IsWhiteSpace(c))]).ToUpperInvariant();
        if (value.Length != 28 || !value.StartsWith("AZ", StringComparison.Ordinal)
            || !value[2..4].All(char.IsAsciiDigit) || !value[4..8].All(char.IsAsciiLetterUpper)
            || !value[8..].All(c => char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c)))
        {
            return null;
        }

        var rearranged = value[4..] + value[..4];
        var remainder = 0;
        foreach (var c in rearranged)
        {
            var digits = char.IsAsciiDigit(c) ? (c - '0').ToString(CultureInfo.InvariantCulture) : (c - 'A' + 10).ToString(CultureInfo.InvariantCulture);
            foreach (var d in digits)
            {
                remainder = ((remainder * 10) + (d - '0')) % 97;
            }
        }

        return remainder == 1 ? value : null;
    }

    /// <summary>Azərbaycan nömrəsi <c>+994XXXXXXXXX</c> formatına salınır (boşluq, tire, mötərizə atılır); düzgün deyilsə <c>null</c>.</summary>
    public static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var value = new string([.. phone.Where(c => c is not (' ' or '-' or '(' or ')'))]);
        if (value.StartsWith('0') && value.Length == 10)
        {
            value = "+994" + value[1..];
        }
        else if (value.StartsWith("994", StringComparison.Ordinal))
        {
            value = "+" + value;
        }

        return value.Length == 13 && value.StartsWith("+994", StringComparison.Ordinal) && value[1..].All(char.IsAsciiDigit) ? value : null;
    }

    /// <summary>Sadə email yoxlaması: bir <c>@</c>, boşluqsuz, domen hissəsində nöqtə, ən çox 254 simvol.</summary>
    public static bool IsEmail(string? value)
    {
        if (value is null || value.Length is < 3 or > 254 || value.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var at = value.IndexOf('@', StringComparison.Ordinal);
        var domain = at > 0 && at == value.LastIndexOf('@') ? value[(at + 1)..] : string.Empty;
        var dot = domain.LastIndexOf('.');
        return dot > 0 && dot < domain.Length - 1 && !domain.Contains("..", StringComparison.Ordinal);
    }

    /// <summary><c>HH:mm</c>, 00:00–23:59.</summary>
    public static bool IsClockTime(string? value) =>
        value is { Length: 5 } && value[2] == ':'
        && int.TryParse(value.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var h) && h < 24
        && int.TryParse(value.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m) && m < 60;

    /// <summary>
    /// Addan URL slug-ı: Azərbaycan hərfləri latın qarşılığına (ə→e, ğ→g, ı→i, ö→o, ü→u, ç→c, ş→s), qalan hərf və
    /// rəqəmlərdən başqa hər şey tireyə çevrilir. Boş qalırsa <c>biznes</c>.
    /// </summary>
    public static string Slugify(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var builder = new StringBuilder(name.Length);
        foreach (var raw in name.Trim())
        {
            var lower = char.ToLowerInvariant(raw);
            var c = lower switch
            {
                'ə' => 'e',
                'ğ' => 'g',
                'ı' or 'İ' => 'i',
                'ö' => 'o',
                'ü' => 'u',
                'ç' => 'c',
                'ş' => 's',
                _ => lower,
            };
            if (char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length > SlugMaxLength)
        {
            slug = slug[..SlugMaxLength].TrimEnd('-');
        }

        return slug.Length == 0 ? "biznes" : slug;
    }
}
