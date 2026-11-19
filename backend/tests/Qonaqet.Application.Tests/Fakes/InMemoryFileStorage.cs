using System.Collections.Concurrent;
using Qonaqet.Application.Abstractions.Services;

namespace Qonaqet.Application.Tests.Fakes;

/// <summary>Yalnız vahid testlər üçün <see cref="IFileStorage"/>: S3 ilə eyni qaydalar (dəyişməz açar, silmə yoxdur).</summary>
public sealed class InMemoryFileStorage : IFileStorage
{
    private readonly ConcurrentDictionary<string, (byte[] Bytes, string ContentType)> _objects = new(StringComparer.Ordinal);

    public string Bucket => "in-memory";

    public IReadOnlyCollection<string> Keys => [.. _objects.Keys];

    public async Task PutAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (!_objects.TryAdd(objectKey, (buffer.ToArray(), contentType)))
        {
            throw new ObjectAlreadyExistsException(objectKey);
        }
    }

    public Task<StoredObject?> OpenReadAsync(string objectKey, CancellationToken cancellationToken) =>
        Task.FromResult(_objects.TryGetValue(objectKey, out var o)
            ? new StoredObject(new MemoryStream(o.Bytes, writable: false), o.ContentType, o.Bytes.LongLength)
            : null);

    public Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken) =>
        Task.FromResult(_objects.ContainsKey(objectKey));
}
