using System.Security.Cryptography;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Domain.Files;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Files.Common;

/// <summary>Yüklənən fayl: klientin elan etdiyi tip və məzmun (ölçü limiti ≤5 MB olduğu üçün yaddaşda).</summary>
public sealed record FileUpload(FilePurpose Purpose, string ContentType, ReadOnlyMemory<byte> Content);

/// <summary>
/// Bütün yükləmə axınlarının (ümumi endpoint, verifikasiya sənədləri, loqolar, CSV-lər) ortaq yolu:
/// təyinat siyasəti üzrə ölçü və MIME yoxlaması (məzmunun magic baytları ilə), SHA-256, dəyişməz açarla
/// saxlamaya yazma və <c>files</c> metadatası. Obyekt Mongo tranzaksiyasından əvvəl yazılır; tranzaksiya
/// uğursuz olsa, istinadsız obyekt saxlamada qalır (heç nə silinmir) və heç bir yerdən görünmür.
/// </summary>
public sealed class FileUploader(IFileStorage storage, IStoredFileRepository files, IClock clock)
{
    public async Task<Result<StoredFile>> UploadAsync(
        FileUpload upload, FileOwnerType ownerType, Guid ownerId, Guid? uploadedBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(upload);
        var policy = FilePolicy.For(upload.Purpose);
        if (upload.Content.IsEmpty)
        {
            return FileErrors.Empty;
        }

        if (upload.Content.Length > policy.MaxSizeBytes)
        {
            return FileErrors.TooLarge;
        }

        var declared = NormalizeContentType(upload.ContentType);
        var detected = FileContentSniffer.Detect(upload.Content.Span);
        if (detected is null || !policy.ContentTypes.Contains(detected) || !IsCompatible(declared, detected))
        {
            return FileErrors.TypeNotAllowed;
        }

        var now = clock.UtcNow;
        var objectKey = ObjectKeys.New(upload.Purpose.ToString(), now);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(upload.Content.Span));
        using (var stream = new MemoryStream(upload.Content.ToArray(), writable: false))
        {
            await storage.PutAsync(objectKey, stream, detected, cancellationToken).ConfigureAwait(false);
        }

        var file = StoredFile.Create(storage.Bucket, objectKey, detected, upload.Content.Length, sha256, ownerType, ownerId, upload.Purpose, now, uploadedBy);
        await files.AddAsync(file, cancellationToken).ConfigureAwait(false);
        return file;
    }

    private static string NormalizeContentType(string? contentType)
    {
        var value = (contentType ?? string.Empty).Split(';', 2)[0].Trim().ToLowerInvariant();
        return value switch
        {
            "image/jpg" or "image/pjpeg" => FilePolicy.Jpeg,
            "application/csv" or "application/vnd.ms-excel" or "text/plain" => FilePolicy.Csv,
            _ => value,
        };
    }

    /// <summary>Elan olunan tip ya məzmunla eynidir, ya da klient tipi bilmir (<c>application/octet-stream</c>).</summary>
    private static bool IsCompatible(string declared, string detected) =>
        declared == detected || declared is "" or "application/octet-stream";
}
