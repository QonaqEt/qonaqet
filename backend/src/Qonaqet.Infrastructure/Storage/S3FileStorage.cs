using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qonaqet.Application.Abstractions.Services;

namespace Qonaqet.Infrastructure.Storage;

/// <summary><c>Storage</c> bölməsi (S3-uyğun: dev/staging/prod-da RustFS).</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string ServiceUrl { get; set; } = "http://localhost:9000";

    public string? AccessKey { get; set; }

    public string? SecretKey { get; set; }

    public string Bucket { get; set; } = "qonaqet-files";

    /// <summary>RustFS və digər S3-uyğun serverlər üçün path-style (<c>host/bucket/key</c>) ünvanlar.</summary>
    public bool ForcePathStyle { get; set; } = true;

    public string Region { get; set; } = "us-east-1";

    /// <summary>
    /// <c>true</c> olanda host başlayanda bucket yaradılır (yoxdursa) və versioning açılır. Compose-da bunu
    /// <c>rustfs-init</c> edir; testlər və tək-proses işə salmalar üçün.
    /// </summary>
    public bool EnsureBucketOnStartup { get; set; }
}

/// <summary>
/// <see cref="IFileStorage"/>-in AWSSDK.S3 tətbiqi. Yalnız S3 API işlədildiyi üçün saxlamanı dəyişmək (Ceph RGW,
/// idarə olunan S3) konfiqurasiya məsələsidir. Silmə əməliyyatı yoxdur; açarlar dəyişməzdir.
/// </summary>
public sealed class S3FileStorage(IAmazonS3 s3, IOptions<StorageOptions> options) : IFileStorage
{
    public string Bucket => options.Value.Bucket;

    public async Task PutAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        if (await ExistsAsync(objectKey, cancellationToken).ConfigureAwait(false))
        {
            throw new ObjectAlreadyExistsException(objectKey);
        }

        var request = new PutObjectRequest
        {
            BucketName = Bucket,
            Key = objectKey,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
        };
        await s3.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StoredObject?> OpenReadAsync(string objectKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        try
        {
            var response = await s3.GetObjectAsync(Bucket, objectKey, cancellationToken).ConfigureAwait(false);
            return new StoredObject(response.ResponseStream, response.Headers.ContentType, response.ContentLength);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        try
        {
            await s3.GetObjectMetadataAsync(Bucket, objectKey, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <summary>Bucket-i yaradır (yoxdursa) və versioning-i açır. İdempotentdir; heç nə silmir.</summary>
    public async Task EnsureBucketAsync(CancellationToken cancellationToken)
    {
        try
        {
            await s3.PutBucketAsync(new PutBucketRequest { BucketName = Bucket }, cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
        {
            // Mövcuddur — toxunmuruq.
        }

        await s3.PutBucketVersioningAsync(
            new PutBucketVersioningRequest
            {
                BucketName = Bucket,
                VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled },
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Bucket-in versioning statusu (<c>Enabled</c> gözlənilir).</summary>
    public async Task<string> GetVersioningStatusAsync(CancellationToken cancellationToken)
    {
        var response = await s3.GetBucketVersioningAsync(new GetBucketVersioningRequest { BucketName = Bucket }, cancellationToken)
            .ConfigureAwait(false);
        return response.VersioningConfig.Status.Value;
    }
}

public static class StorageRegistration
{
    public static IServiceCollection AddFileStorage(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.AddSingleton<IAmazonS3>(sp => CreateClient(sp.GetRequiredService<IOptions<StorageOptions>>().Value));
        services.AddSingleton<S3FileStorage>();
        services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<S3FileStorage>());
        services.AddHostedService<StorageBucketInitializer>();
        return services;
    }

    public static AmazonS3Client CreateClient(StorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var config = new AmazonS3Config
        {
            ServiceURL = options.ServiceUrl,
            ForcePathStyle = options.ForcePathStyle,
            AuthenticationRegion = options.Region,

            // S3-uyğun serverlər yeni "flexible checksum" başlıqlarını həmişə dəstəkləmir; yalnız tələb olunanda.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };

        return string.IsNullOrEmpty(options.AccessKey)
            ? new AmazonS3Client(new AnonymousAWSCredentials(), config)
            : new AmazonS3Client(new BasicAWSCredentials(options.AccessKey, options.SecretKey), config);
    }
}

/// <summary><see cref="StorageOptions.EnsureBucketOnStartup"/> açıq olanda bucket-i hazırlayır.</summary>
internal sealed partial class StorageBucketInitializer(
    S3FileStorage storage, IOptions<StorageOptions> options, ILogger<StorageBucketInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.EnsureBucketOnStartup)
        {
            return;
        }

        await storage.EnsureBucketAsync(cancellationToken).ConfigureAwait(false);
        LogReady(logger, storage.Bucket);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Fayl bucket-i hazırdır (versioning açıq): {Bucket}")]
    private static partial void LogReady(ILogger logger, string bucket);
}
