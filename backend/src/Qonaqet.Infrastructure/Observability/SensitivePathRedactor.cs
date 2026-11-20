using System.Diagnostics;
using System.Text.RegularExpressions;
using OpenTelemetry;

namespace Qonaqet.Infrastructure.Observability;

/// <summary>
/// Trace-lərdən bearer token-ləri olan yolları maskalayır: <c>/public/gifts/{token}</c> → <c>/public/gifts/***</c>. ASP.NET Core
/// <c>url.path</c>/<c>url.full</c> teqlərinə faktiki yolu yazır; hədiyyə linki isə bearer credential-dır və heç vaxt OTLP-yə getməməlidir.
/// </summary>
public sealed partial class SensitivePathRedactor : BaseProcessor<Activity>
{
    public const string Mask = "***";

    public override void OnEnd(Activity data)
    {
        ArgumentNullException.ThrowIfNull(data);
        data.DisplayName = Redact(data.DisplayName);
        foreach (var tag in data.TagObjects.ToArray())
        {
            if (tag.Value is string value && Redact(value) is var redacted && !ReferenceEquals(redacted, value))
            {
                data.SetTag(tag.Key, redacted);
            }
        }
    }

    /// <summary>Mətndə token hissəsi varsa maskalanmış yeni sətir, yoxsa eyni obyekt.</summary>
    public static string Redact(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Contains("/public/gifts/", StringComparison.OrdinalIgnoreCase) || value.Contains("/g/", StringComparison.Ordinal)
            ? TokenSegment().Replace(value, m => m.Groups[1].Value + Mask)
            : value;
    }

    [GeneratedRegex(@"(/public/gifts/|/g/)(?!\*\*\*)[^/?#\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TokenSegment();
}
