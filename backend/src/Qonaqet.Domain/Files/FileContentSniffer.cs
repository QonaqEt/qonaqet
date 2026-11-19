using System.Text;

namespace Qonaqet.Domain.Files;

/// <summary>
/// Faylın həqiqi tipini məzmunun ilk baytlarından (magic bytes) təyin edir. Klientin dediyi MIME tipinə etibar edilmir:
/// elan olunan tip bununla üst-üstə düşməlidir.
/// </summary>
public static class FileContentSniffer
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Tanınan tip (<see cref="FilePolicy"/> sabitləri) və ya <c>null</c>.</summary>
    public static string? Detect(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith(PdfMagic))
        {
            return FilePolicy.Pdf;
        }

        if (content.StartsWith(PngMagic))
        {
            return FilePolicy.Png;
        }

        if (content.StartsWith(JpegMagic))
        {
            return FilePolicy.Jpeg;
        }

        return IsText(content) ? FilePolicy.Csv : null;
    }

    /// <summary>Etibarlı UTF-8, NUL baytı yoxdur — mətn (CSV) faylı.</summary>
    private static bool IsText(ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty || content.Contains((byte)0))
        {
            return false;
        }

        try
        {
            _ = StrictUtf8.GetCharCount(content);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
