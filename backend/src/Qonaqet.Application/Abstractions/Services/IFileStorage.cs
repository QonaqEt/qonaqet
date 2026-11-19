namespace Qonaqet.Application.Abstractions.Services;

/// <summary>
/// S3-uyğun obyekt saxlama (RustFS). Obyekt açarları <b>dəyişməzdir</b>: eyni açara ikinci dəfə yazmaq xətadır,
/// bucket-lərdə versioning açıqdır və heç bir obyekt silinmir (silmə metodu qəsdən yoxdur).
/// Fayllara domen səviyyəsində yalnız <c>fileId</c> ilə istinad olunur; açar yalnız <c>files</c> metadatasında saxlanılır.
/// </summary>
public interface IFileStorage
{
    /// <summary>Faylların yazıldığı bucket.</summary>
    string Bucket { get; }

    /// <summary>Yeni obyekt yazır. Açar artıq mövcuddursa <see cref="ObjectAlreadyExistsException"/> atılır.</summary>
    Task PutAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken);

    /// <summary>Obyekti oxumaq üçün açır; yoxdursa <c>null</c>. Çağıran stream-i bağlayır.</summary>
    Task<StoredObject?> OpenReadAsync(string objectKey, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken);
}

/// <summary>Oxunan obyekt: məzmun stream-i və metadata.</summary>
public sealed record StoredObject(Stream Content, string ContentType, long SizeBytes) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

public sealed class ObjectAlreadyExistsException(string objectKey)
    : InvalidOperationException($"Obyekt açarı dəyişməzdir və artıq mövcuddur: {objectKey}")
{
    public string ObjectKey { get; } = objectKey;
}

/// <summary>Dəyişməz obyekt açarları: <c>{purpose}/{yyyy}/{MM}/{guid}</c> — təkrarlanmır, heç vaxt yenidən yazılmır.</summary>
public static class ObjectKeys
{
    public static string New(string purpose, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        var prefix = purpose.ToLowerInvariant();
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{prefix}/{now.UtcDateTime:yyyy}/{now.UtcDateTime:MM}/{Guid.CreateVersion7(now):N}");
    }
}
