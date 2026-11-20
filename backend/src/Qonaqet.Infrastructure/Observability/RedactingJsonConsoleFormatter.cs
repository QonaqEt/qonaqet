using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Qonaqet.Infrastructure.Observability;

/// <summary>
/// JSON console formatter (bir sətir = bir JSON obyekt). Adı həssas olan struktur sahələrinin (OTP, token,
/// parol, FIN, kart, IBAN, e-poçt …) dəyərləri həm <c>state</c>-də, həm də render olunan mesajda
/// <c>***</c> ilə əvəzlənir — dəyər səhvən log çağırışına düşsə belə çıxışa yazılmır. Bütün mətn dəyərlərində (o cümlədən ASP.NET-in
/// <c>RequestPath</c> scope-unda) bearer token daşıyan yollar (<c>/public/gifts/{token}</c>) <see cref="SensitivePathRedactor"/> ilə maskalanır.
/// </summary>
public sealed partial class RedactingJsonConsoleFormatter() : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "qonaqet-json";
    public const string Mask = "***";

    // Log mətni HTML-də göstərilmir: Azərbaycan hərfləri və "+" escape olunmadan oxunaqlı qalsın.
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly string[] SensitiveFragments =
        ["password", "secret", "token", "otp", "authorization", "cookie", "cvv", "iban", "email", "phone", "apikey"];

    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "fin", "finhash", "card", "cardnumber", "pan", "code", "shortcode", "key",
    };

    /// <summary>Sahə adı həssasdırmı (böyük/kiçik hərfdən və <c>_</c>/<c>-</c>-dən asılı deyil).</summary>
    public static bool IsSensitive(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var normalized = name.Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).Replace("@", "", StringComparison.Ordinal);
        return SensitiveNames.Contains(normalized)
            || SensitiveFragments.Any(f => normalized.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        ArgumentNullException.ThrowIfNull(textWriter);
        var state = logEntry.State as IEnumerable<KeyValuePair<string, object?>>;
        var properties = state?.Where(p => p.Key != "{OriginalFormat}").ToArray() ?? [];
        var template = state?.FirstOrDefault(p => p.Key == "{OriginalFormat}").Value as string;

        var message = template is not null
            ? Render(template, properties)
            : logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception) ?? string.Empty;

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, WriterOptions))
        {
            json.WriteStartObject();
            json.WriteString("timestamp", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            json.WriteString("level", logEntry.LogLevel.ToString());
            json.WriteString("category", logEntry.Category);
            json.WriteNumber("eventId", logEntry.EventId.Id);
            json.WriteString("message", SensitivePathRedactor.Redact(message));
            if (logEntry.Exception is { } exception)
            {
                json.WriteString("exception", SensitivePathRedactor.Redact(exception.ToString()));
            }

            json.WriteStartObject("state");
            foreach (var (key, value) in properties)
            {
                json.WriteString(key, IsSensitive(key) ? Mask : Text(value));
            }

            json.WriteEndObject();

            json.WriteStartObject("scopes");
            scopeProvider?.ForEachScope(
                (scope, writer) =>
                {
                    if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                    {
                        foreach (var (key, value) in pairs.Where(p => p.Key != "{OriginalFormat}"))
                        {
                            writer.WriteString(key, IsSensitive(key) ? Mask : Text(value));
                        }
                    }
                },
                json);
            json.WriteEndObject();
            json.WriteEndObject();
        }

        textWriter.Write(Encoding.UTF8.GetString(buffer.ToArray()));
        textWriter.Write(Environment.NewLine);
    }

    private static string? Text(object? value) =>
        Convert.ToString(value, CultureInfo.InvariantCulture) is { } text ? SensitivePathRedactor.Redact(text) : null;

    /// <summary><c>{Name}</c> / <c>{Name:format}</c> yer tutucularını (həssaslar maskalanmaqla) doldurur.</summary>
    public static string Render(string template, IReadOnlyList<KeyValuePair<string, object?>> properties)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(properties);
        var index = 0;
        return Placeholder().Replace(template, match =>
        {
            var name = match.Groups["name"].Value;
            var format = match.Groups["format"].Success ? match.Groups["format"].Value : null;
            var value = properties.FirstOrDefault(p => p.Key == name).Value ?? (index < properties.Count ? properties[index].Value : null);
            index++;
            if (IsSensitive(name))
            {
                return Mask;
            }

            return value is IFormattable formattable && format is not null
                ? formattable.ToString(format, CultureInfo.InvariantCulture)
                : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "(null)";
        });
    }

    [GeneratedRegex(@"\{(?<name>[^{}:,]+)(,[^{}:]*)?(:(?<format>[^{}]*))?\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}
