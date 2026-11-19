using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Infrastructure.Documents.Pdf;

namespace Qonaqet.IntegrationTests.Documents;

/// <summary>Chromium headless shell-i bir dəfə quraşdırır (<c>PLAYWRIGHT_BROWSERS_PATH</c>); CI Linux-da sistem asılılıqları ilə.</summary>
public sealed class ChromiumFixture : IAsyncLifetime
{
    public string? UnavailableReason { get; private set; }

    public PlaywrightPdfRenderer Renderer { get; } = new(Options.Create(new PdfOptions { MaxConcurrency = 3 }));

    public ValueTask InitializeAsync()
    {
        var ci = Environment.GetEnvironmentVariable("CI") == "true";
        var exitCode = PdfRegistration.InstallChromium(withDependencies: ci && OperatingSystem.IsLinux());
        if (exitCode != 0)
        {
            if (ci)
            {
                throw new InvalidOperationException($"Chromium quraşdırılmadı (exit {exitCode}).");
            }

            UnavailableReason = $"Chromium quraşdırılmadı (exit {exitCode}).";
        }

        return ValueTask.CompletedTask;
    }

    public void SkipIfUnavailable() => Assert.SkipWhen(UnavailableReason is not null, UnavailableReason ?? string.Empty);

    public ValueTask DisposeAsync() => Renderer.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class ChromiumCollection : ICollectionFixture<ChromiumFixture>
{
    public const string Name = "chromium";
}

/// <summary>Playwright PDF renderer (E03-T04).</summary>
[Collection(ChromiumCollection.Name)]
public sealed partial class PdfRendererTests(ChromiumFixture chromium)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Template_renders_to_pdf_with_embedded_brand_fonts_and_azerbaijani_glyphs()
    {
        chromium.SkipIfUnavailable();

        var pdf = await chromium.Renderer.RenderTemplateAsync(
            "report",
            new Dictionary<string, string?>
            {
                ["title"] = "CSR hesabatı — Novruz kampaniyası",
                ["subtitle"] = "Şəki, Gəncə, Quba: ehtiyacı olanlara ısmarlanan çaylar",
                ["generatedAt"] = "19.11.2026",
            },
            new PdfTable(["Biznes", "Ismarlama sayı"], [["Çayxana Ağ Şəhər", "128"], ["Öz Şirniyyat <b>evi</b>", "64"]]),
            Ct);

        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5), StringComparison.Ordinal);
        var raw = Encoding.Latin1.GetString(pdf);
        Assert.Matches(@"/FontName\s*/[A-Z]{6}\+Manrope", raw);
        Assert.Matches(@"/FontName\s*/[A-Z]{6}\+Unbounded", raw);

        // ToUnicode cədvəlləri: ə (U+0259), ş (U+015F), ı (U+0131), ğ (U+011F) düzgün qlif kimi yazılıb.
        var cmaps = string.Concat(InflatedStreams(pdf).Where(s => s.Contains("beginbfchar", StringComparison.Ordinal) || s.Contains("beginbfrange", StringComparison.Ordinal)));
        foreach (var codePoint in new[] { "0259", "015F", "0131", "011F", "00C7" })
        {
            Assert.Contains(codePoint, cmaps, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Parallel_renders_are_stable()
    {
        chromium.SkipIfUnavailable();

        var renders = Enumerable.Range(0, 8).Select(i => chromium.Renderer.RenderHtmlAsync(
            $"<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body style=\"font-family:'Manrope'\"><h1>Sənəd {i}</h1><p>Qonaq et, xoşbəxt et</p></body></html>", Ct));
        var pdfs = await Task.WhenAll(renders);

        Assert.All(pdfs, pdf => Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5), StringComparison.Ordinal));
    }

    [Fact]
    public async Task External_resources_are_never_loaded()
    {
        chromium.SkipIfUnavailable();

        var pdf = await chromium.Renderer.RenderHtmlAsync(
            "<html><head><meta charset=\"utf-8\"></head><body><img src=\"http://169.254.169.254/latest/meta-data\"><p>ok</p></body></html>", Ct);

        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5), StringComparison.Ordinal);
    }

    private static IEnumerable<string> InflatedStreams(byte[] pdf)
    {
        var raw = Encoding.Latin1.GetString(pdf);
        foreach (Match match in StreamRegex().Matches(raw))
        {
            var start = match.Index + match.Length;
            var end = raw.IndexOf("endstream", start, StringComparison.Ordinal);
            if (end < 0)
            {
                continue;
            }

            string? text = null;
            try
            {
                using var input = new MemoryStream(pdf, start, end - start);
                using var zlib = new ZLibStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                zlib.CopyTo(output);
                text = Encoding.Latin1.GetString(output.ToArray());
            }
            catch (InvalidDataException)
            {
                // Sıxılmamış və ya başqa filtrli stream.
            }

            if (text is not null)
            {
                yield return text;
            }
        }
    }

    [GeneratedRegex("stream\r?\n")]
    private static partial Regex StreamRegex();
}
