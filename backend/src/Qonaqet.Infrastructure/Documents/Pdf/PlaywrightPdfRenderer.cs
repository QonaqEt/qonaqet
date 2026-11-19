using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Infrastructure.Email;

namespace Qonaqet.Infrastructure.Documents.Pdf;

/// <summary><c>Pdf</c> bölməsi.</summary>
public sealed class PdfOptions
{
    public const string SectionName = "Pdf";

    /// <summary>Eyni anda açıq səhifə sayı (Chromium yaddaşını qorumaq üçün).</summary>
    public int MaxConcurrency { get; set; } = 4;

    public TimeSpan RenderTimeout { get; set; } = TimeSpan.FromSeconds(60);
}

/// <summary>
/// Playwright + headless Chromium. Brauzer tənbəl və bir dəfə açılır (bütün sorğular paylaşır), hər render öz
/// izolə olunmuş kontekstini/səhifəsini alır; paralellik <see cref="PdfOptions.MaxConcurrency"/> ilə məhdudlaşır.
/// Brauzer çökərsə (disconnected), növbəti sorğu yenisini açır. Şriftlər data URI ilə daxil edilir — sistem
/// şriftlərindən asılı deyil, Azərbaycan hərfləri (ə ğ ı ö ü ç ş) hər mühitdə eyni çıxır.
/// </summary>
public sealed class PlaywrightPdfRenderer(IOptions<PdfOptions> options) : IPdfRenderer, IAsyncDisposable
{
    private const string TableMarker = "<!--TABLE-->";
    private static readonly Lazy<string> FontFaces = new(BuildFontFaces);

    private readonly SemaphoreSlim _launchLock = new(1, 1);
    private readonly SemaphoreSlim _slots = new(Math.Max(1, options.Value.MaxConcurrency));
    private readonly EmailTemplateRenderer _tokens = new();
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public async Task<byte[]> RenderHtmlAsync(string html, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(html);
        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var browser = await GetBrowserAsync(cancellationToken).ConfigureAwait(false);
            await using var context = await browser.NewContextAsync().ConfigureAwait(false);
            var page = await context.NewPageAsync().ConfigureAwait(false);
            var timeout = (float)options.Value.RenderTimeout.TotalMilliseconds;
            page.SetDefaultTimeout(timeout);

            // Şablonlar xarici resurs yükləmir; şəbəkə tam bağlıdır (SSRF / sızma yoxdur).
            await page.RouteAsync("**/*", route => route.AbortAsync()).ConfigureAwait(false);
            await page.SetContentAsync(InjectFonts(html), new PageSetContentOptions { WaitUntil = WaitUntilState.Load }).ConfigureAwait(false);
            await page.EvaluateAsync("() => document.fonts.ready").ConfigureAwait(false);
            return await page.PdfAsync(new PagePdfOptions
            {
                Format = "A4",
                PrintBackground = true,
                Margin = new Margin { Top = "16mm", Bottom = "16mm", Left = "14mm", Right = "14mm" },
            }).ConfigureAwait(false);
        }
        finally
        {
            _slots.Release();
        }
    }

    public Task<byte[]> RenderTemplateAsync(
        string templateName, IReadOnlyDictionary<string, string?> model, PdfTable? table, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateName);
        ArgumentNullException.ThrowIfNull(model);
        var html = TemplateEngine.Render(ReadTemplate(templateName), key => _tokens.Resolve(key, model), htmlEncode: true);
        if (table is not null)
        {
            html = html.Replace(TableMarker, TableHtml(table), StringComparison.Ordinal);
        }

        return RenderHtmlAsync(html, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync().ConfigureAwait(false);
        }

        _playwright?.Dispose();
        _launchLock.Dispose();
        _slots.Dispose();
    }

    private async Task<IBrowser> GetBrowserAsync(CancellationToken cancellationToken)
    {
        if (_browser is { IsConnected: true } ready)
        {
            return ready;
        }

        await _launchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_browser is { IsConnected: true } existing)
            {
                return existing;
            }

            if (_browser is not null)
            {
                await _browser.DisposeAsync().ConfigureAwait(false);
            }

            _playwright ??= await Playwright.CreateAsync().ConfigureAwait(false);
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true }).ConfigureAwait(false);
            return _browser;
        }
        finally
        {
            _launchLock.Release();
        }
    }

    private static string InjectFonts(string html)
    {
        var style = $"<style>{FontFaces.Value}</style>";
        var head = html.IndexOf("<head>", StringComparison.OrdinalIgnoreCase);
        return head >= 0 ? html.Insert(head + "<head>".Length, style) : style + html;
    }

    /// <summary>Cədvəlin HTML-i; başlıq və xanalar HTML-encode olunur.</summary>
    public static string TableHtml(PdfTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var sb = new StringBuilder("<table class=\"data\"><thead><tr>");
        foreach (var header in table.Headers)
        {
            sb.Append("<th>").Append(WebUtility.HtmlEncode(header)).Append("</th>");
        }

        sb.Append("</tr></thead><tbody>");
        foreach (var row in table.Rows)
        {
            sb.Append("<tr>");
            foreach (var cell in row)
            {
                sb.Append("<td>").Append(WebUtility.HtmlEncode(cell)).Append("</td>");
            }

            sb.Append("</tr>");
        }

        return sb.Append("</tbody></table>").ToString();
    }

    private static string BuildFontFaces()
    {
        static string Face(string family, string file) =>
            $"@font-face{{font-family:'{family}';src:url(data:font/ttf;base64,{Convert.ToBase64String(ReadResource($"Fonts.{file}"))}) format('truetype');font-weight:100 900;font-style:normal;}}";

        return Face("Manrope", "Manrope.ttf") + Face("Unbounded", "Unbounded.ttf");
    }

    private static string ReadTemplate(string name) => Encoding.UTF8.GetString(ReadResource($"Templates.{name}.html"));

    private static byte[] ReadResource(string relative)
    {
        var name = $"Qonaqet.Infrastructure.Documents.Pdf.{relative}";
        using var stream = typeof(PlaywrightPdfRenderer).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"PDF resursu tapılmadı: {relative}");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}

public static class PdfRegistration
{
    /// <summary>Yalnız Worker: Chromium yalnız Worker image-ində quraşdırılır (<c>--install-chromium</c>).</summary>
    public static IServiceCollection AddPdfRenderer(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<PdfOptions>(configuration.GetSection(PdfOptions.SectionName));
        services.AddSingleton<IPdfRenderer, PlaywrightPdfRenderer>();
        return services;
    }

    /// <summary>
    /// Chromium headless shell-i (və Linux-da sistem asılılıqlarını) quraşdırır; Worker Dockerfile-ı və testlər çağırır.
    /// Yer: <c>PLAYWRIGHT_BROWSERS_PATH</c>.
    /// </summary>
    public static int InstallChromium(bool withDependencies) =>
        Microsoft.Playwright.Program.Main(withDependencies
            ? ["install", "--with-deps", "--only-shell", "chromium"]
            : ["install", "--only-shell", "chromium"]);
}
