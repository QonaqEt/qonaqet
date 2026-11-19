using System.Text;
using Qonaqet.Application.Abstractions.Services;

namespace Qonaqet.IntegrationTests.Storage;

/// <summary>S3FileStorage real RustFS-ə qarşı (E03-T01).</summary>
[Collection(RustFsCollection.Name)]
public sealed class S3FileStorageTests(RustFsFixture rustfs)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Bucket_has_versioning_enabled_and_ensure_is_idempotent()
    {
        rustfs.SkipIfUnavailable();
        var storage = await rustfs.NewStorageAsync();

        await storage.EnsureBucketAsync(Ct);

        Assert.Equal("Enabled", await storage.GetVersioningStatusAsync(Ct));
    }

    [Fact]
    public async Task Object_round_trips_with_azerbaijani_content_and_content_type()
    {
        rustfs.SkipIfUnavailable();
        var storage = await rustfs.NewStorageAsync();
        var key = ObjectKeys.New("VerificationDocument", DateTimeOffset.UtcNow);
        var text = "Qonaq et, xoşbəxt et — ə ğ ı ö ü ç ş";

        await using (var body = new MemoryStream(Encoding.UTF8.GetBytes(text)))
        {
            await storage.PutAsync(key, body, "application/pdf", Ct);
        }

        Assert.True(await storage.ExistsAsync(key, Ct));
        await using var stored = await storage.OpenReadAsync(key, Ct);
        Assert.NotNull(stored);
        Assert.Equal("application/pdf", stored.ContentType);
        using var reader = new StreamReader(stored.Content, Encoding.UTF8);
        Assert.Equal(text, await reader.ReadToEndAsync(Ct));
    }

    [Fact]
    public async Task Writing_an_existing_key_is_rejected_and_the_original_is_kept()
    {
        rustfs.SkipIfUnavailable();
        var storage = await rustfs.NewStorageAsync();
        var key = ObjectKeys.New("Logo", DateTimeOffset.UtcNow);
        await using (var first = new MemoryStream([1, 2, 3]))
        {
            await storage.PutAsync(key, first, "image/png", Ct);
        }

        await using var second = new MemoryStream([9, 9]);
        await Assert.ThrowsAsync<ObjectAlreadyExistsException>(() => storage.PutAsync(key, second, "image/png", Ct));

        await using var stored = await storage.OpenReadAsync(key, Ct);
        Assert.Equal(3, stored!.SizeBytes);
    }

    [Fact]
    public async Task Missing_object_returns_null()
    {
        rustfs.SkipIfUnavailable();
        var storage = await rustfs.NewStorageAsync();

        Assert.Null(await storage.OpenReadAsync("logo/2026/11/missing", Ct));
        Assert.False(await storage.ExistsAsync("logo/2026/11/missing", Ct));
    }
}
