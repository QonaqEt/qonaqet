using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Qonaqet.Application.Abstractions.Services;

namespace Qonaqet.Infrastructure.Email;

/// <summary>
/// Minimal <c>{{placeholder}}</c> mühərriki. HTML rejimində <b>hər</b> dəyər HTML-encode olunur — xam HTML daxil
/// etmək üçün yol yoxdur (XSS mümkün deyil). Naməlum placeholder xətadır ki, şablon səhvi səssiz qalmasın.
/// </summary>
public static partial class TemplateEngine
{
    public static string Render(string template, Func<string, string?> resolve, bool htmlEncode)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(resolve);
        return Placeholder().Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            var value = resolve(key) ?? throw new KeyNotFoundException($"Şablon dəyəri verilməyib: {key}");
            return htmlEncode ? WebUtility.HtmlEncode(value) : value;
        });
    }

    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_.]+)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}

/// <summary>
/// Embedded resource şablonları: <c>Templates/{name}.subject.txt</c>, <c>{name}.html</c>, <c>{name}.txt</c> və ortaq
/// <c>_layout.html</c>. Model dəyərlərindən əlavə <c>token.*</c> açarları dizayn token-ləridir (<c>token.color.brand.nar</c>);
/// <c>tokenOf.X</c> isə token yolunu model dəyərindən götürür (dinamik tema rəngi).
/// </summary>
public sealed class EmailTemplateRenderer : IEmailTemplateRenderer
{
    private const string ContentMarker = "<!--CONTENT-->";
    private static readonly Assembly Assembly = typeof(EmailTemplateRenderer).Assembly;

    private readonly IReadOnlyDictionary<string, string> _tokens = DesignTokens.Flatten();

    public RenderedEmail Render(string templateName, IReadOnlyDictionary<string, string?> model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateName);
        ArgumentNullException.ThrowIfNull(model);

        string? Resolve(string key) => this.Resolve(key, model);

        var subject = TemplateEngine.Render(Read($"{templateName}.subject.txt").Trim(), Resolve, htmlEncode: false);
        var content = TemplateEngine.Render(Read($"{templateName}.html"), Resolve, htmlEncode: true);
        var layout = TemplateEngine.Render(Read("_layout.html"), key => key == "subject" ? subject : Resolve(key), htmlEncode: true);
        var text = TemplateEngine.Render(Read($"{templateName}.txt"), Resolve, htmlEncode: false);
        return new RenderedEmail(subject, layout.Replace(ContentMarker, content, StringComparison.Ordinal), text);
    }

    /// <summary>Model dəyəri və ya <c>token.*</c> dizayn token-i; tapılmasa <c>null</c> (PDF şablonları da işlədir).</summary>
    public string? Resolve(string key, IReadOnlyDictionary<string, string?> model)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(model);
        // tokenOf.X: X model dəyəridir və token yoludur (məs. hədiyyə temasının rəngi: themeToken = "color.theme.family").
        return key.StartsWith("token.", StringComparison.Ordinal) ? _tokens.GetValueOrDefault(key[6..])
            : key.StartsWith("tokenOf.", StringComparison.Ordinal)
                ? model.TryGetValue(key[8..], out var path) && path is not null ? _tokens.GetValueOrDefault(path) : null
            : model.TryGetValue(key, out var value) ? value ?? string.Empty
            : null;
    }

    private static string Read(string file)
    {
        var name = $"Qonaqet.Infrastructure.Email.Templates.{file}";
        using var stream = Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Email şablonu tapılmadı: {file}");
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }
}

/// <summary>
/// <c>design-tokens/tokens.json</c> (tək mənbə) assembly-yə embedded olunur; burada düz açarlara çevrilir və
/// <c>{color.brand.saffron}</c> kimi istinadlar həll olunur. Email-lərdə rəng hard-code olunmur.
/// </summary>
public static class DesignTokens
{
    public static IReadOnlyDictionary<string, string> Flatten()
    {
        using var stream = typeof(DesignTokens).Assembly.GetManifestResourceStream("Qonaqet.DesignTokens.json")
            ?? throw new InvalidOperationException("design-tokens/tokens.json embedded deyil.");
        using var document = JsonDocument.Parse(stream);
        var raw = new Dictionary<string, string>(StringComparer.Ordinal);
        Walk(document.RootElement, string.Empty, raw);

        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in raw.Keys)
        {
            resolved[key] = ResolveReference(raw, raw[key], depth: 0);
        }

        return resolved;
    }

    private static void Walk(JsonElement element, string path, Dictionary<string, string> into)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (element.TryGetProperty("$value", out var value))
        {
            into[path] = value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
            return;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!property.Name.StartsWith('$'))
            {
                Walk(property.Value, path.Length == 0 ? property.Name : $"{path}.{property.Name}", into);
            }
        }
    }

    private static string ResolveReference(Dictionary<string, string> raw, string value, int depth)
    {
        if (depth > 10 || value.Length < 3 || value[0] != '{' || value[^1] != '}')
        {
            return value;
        }

        return raw.TryGetValue(value[1..^1], out var target) ? ResolveReference(raw, target, depth + 1) : value;
    }
}
