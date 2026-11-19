namespace Qonaqet.Application.Abstractions.Services;

/// <summary>
/// HTML → PDF (Worker-də headless Chromium, Playwright). Brend şriftləri (Manrope, Unbounded) renderer tərəfindən
/// səhifəyə daxil edilir; şablonlar <c>font-family: 'Manrope'</c> yazmaqla kifayətlənir.
/// </summary>
public interface IPdfRenderer
{
    /// <summary>Hazır HTML sənədindən A4 PDF.</summary>
    Task<byte[]> RenderHtmlAsync(string html, CancellationToken cancellationToken);

    /// <summary>
    /// Embedded şablondan (<c>Documents/Pdf/Templates/{name}.html</c>) PDF: dəyərlər HTML-encode olunur,
    /// <c>{{token.*}}</c> dizayn token-ləridir. Cədvəl kimi təkrarlanan hissələr çağıran tərəfindən hazırlanmır —
    /// şablon mühərriki xam HTML qəbul etmir; sətirlər üçün <see cref="PdfTable"/> işlədilir.
    /// </summary>
    Task<byte[]> RenderTemplateAsync(string templateName, IReadOnlyDictionary<string, string?> model, PdfTable? table, CancellationToken cancellationToken);
}

/// <summary>Şablondakı <c>&lt;!--TABLE--&gt;</c> yerinə qoyulan cədvəl; xanalar HTML-encode olunur.</summary>
public sealed record PdfTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);
