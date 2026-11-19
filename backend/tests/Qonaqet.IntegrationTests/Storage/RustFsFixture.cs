using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Options;
using Qonaqet.Infrastructure.Storage;

namespace Qonaqet.IntegrationTests.Storage;

/// <summary>
/// RustFS Testcontainers-in generic konteyneri ilə; image <c>deploy/docker-compose.yml</c>-dəki ilə eyni pinlənib (ADR 0002).
/// Docker yoxdursa lokal testlər skip olunur, CI-da isə xəta verir.
/// </summary>
public sealed class RustFsFixture : IAsyncLifetime
{
    public const string Image = "rustfs/rustfs:1.0.5";
    public const string AccessKey = "qonaqet-test";
    public const string SecretKey = "qonaqet-test-secret";

    private IContainer? _container;

    public string? UnavailableReason { get; private set; }

    public string ServiceUrl { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new ContainerBuilder(Image)
                .WithEnvironment("RUSTFS_ACCESS_KEY", AccessKey)
                .WithEnvironment("RUSTFS_SECRET_KEY", SecretKey)
                .WithPortBinding(9000, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(9000).ForPath("/health")))
                .Build();
            await _container.StartAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (Environment.GetEnvironmentVariable("CI") != "true")
        {
            UnavailableReason = $"Docker əlçatan deyil: {ex.Message}";
            return;
        }

        ServiceUrl = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(9000)}";
    }

    public StorageOptions Options(string? bucket = null) => new()
    {
        ServiceUrl = ServiceUrl,
        AccessKey = AccessKey,
        SecretKey = SecretKey,
        Bucket = bucket ?? "t-" + Guid.NewGuid().ToString("N")[..12],
        ForcePathStyle = true,
    };

    /// <summary>Təmiz, versioning-i açıq bucket üzərində saxlama.</summary>
    public async Task<S3FileStorage> NewStorageAsync()
    {
        var options = Options();
        var storage = new S3FileStorage(StorageRegistration.CreateClient(options), Microsoft.Extensions.Options.Options.Create(options));
        await storage.EnsureBucketAsync(TestContext.Current.CancellationToken);
        return storage;
    }

    public void SkipIfUnavailable() => Assert.SkipWhen(UnavailableReason is not null, UnavailableReason ?? string.Empty);

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }
}

[CollectionDefinition(Name)]
public sealed class RustFsCollection : ICollectionFixture<RustFsFixture>
{
    public const string Name = "rustfs";
}
