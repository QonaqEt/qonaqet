using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Files;

public readonly record struct FileId(Guid Value) : IStronglyTypedId<FileId>
{
    public static FileId From(Guid value) => new(value);
}

/// <summary>Faylın təyinatı (<c>CLAUDE.md</c> → <c>files.purpose</c>). Hər təyinatın öz qaydası var: <see cref="FilePolicy"/>.</summary>
public enum FilePurpose
{
    VerificationDocument,
    Logo,
    Banner,
    CatalogImage,
    CouponCsv,
    RecipientCsv,
    GiftImage,
}

/// <summary>Faylın sahibi olan obyekt növü.</summary>
public enum FileOwnerType
{
    User,
    Business,
    Category,
    Corporate,
    CorporateGiftOrder,
    Brand,
    Coupon,
    Campaign,
    VerificationRequest,
}

/// <summary>
/// Saxlanmış faylın metadatası (<c>files</c>). Məzmun obyekt saxlamasında dəyişməz açarla durur; domen hər yerdə
/// yalnız <see cref="FileId"/> ilə istinad edir, xam obyekt açarı API-dən heç vaxt çıxmır.
/// Soft delete olunmuş fayl heç bir endpoint-dən verilmir, obyekt isə saxlamada qalır (heç nə silinmir).
/// </summary>
public sealed class StoredFile : AggregateRoot<FileId>
{
    private StoredFile(
        FileId id, string bucket, string objectKey, string contentType, long sizeBytes, string sha256,
        FileOwnerType ownerType, Guid ownerId, FilePurpose purpose, DateTimeOffset createdAt, Guid? createdBy)
        : base(id, createdAt, createdBy)
    {
        Bucket = bucket;
        ObjectKey = objectKey;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
        OwnerType = ownerType;
        OwnerId = ownerId;
        Purpose = purpose;
    }

    private StoredFile()
    {
    }

    public string Bucket { get; private set; } = string.Empty;

    /// <summary>Dəyişməz obyekt açarı (yalnız infrastruktur oxuyur).</summary>
    public string ObjectKey { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    /// <summary>Məzmunun SHA-256 hash-i (hex, kiçik hərf).</summary>
    public string Sha256 { get; private set; } = string.Empty;

    public FileOwnerType OwnerType { get; private set; }

    public Guid OwnerId { get; private set; }

    public FilePurpose Purpose { get; private set; }

    public static StoredFile Create(
        string bucket, string objectKey, string contentType, long sizeBytes, string sha256,
        FileOwnerType ownerType, Guid ownerId, FilePurpose purpose, DateTimeOffset at, Guid? createdBy)
    {
        Guard.NotNullOrWhiteSpace(bucket);
        Guard.NotNullOrWhiteSpace(objectKey);
        Guard.NotNullOrWhiteSpace(contentType);
        Guard.NotNullOrWhiteSpace(sha256);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeBytes);
        return new StoredFile(
            StronglyTypedId.New<FileId>(), bucket, objectKey, contentType, sizeBytes, sha256, ownerType, ownerId, purpose, at, createdBy);
    }

    /// <summary>Ictimai təyinatlı fayllar (loqo, banner, kataloq/hədiyyə şəkli) anonim oxuna bilər.</summary>
    public bool IsPublic => FilePolicy.For(Purpose).IsPublic;
}

/// <summary>Təyinat üzrə qaydalar: icazəli MIME tipləri, maksimum ölçü və ictimai olub-olmaması.</summary>
public sealed record FilePolicy(IReadOnlyList<string> ContentTypes, long MaxSizeBytes, bool IsPublic)
{
    public const long FiveMegabytes = 5L * 1024 * 1024;

    public const string Pdf = "application/pdf";
    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string Csv = "text/csv";

    private static readonly FilePolicy Images = new([Jpeg, Png], FiveMegabytes, IsPublic: true);
    private static readonly FilePolicy Documents = new([Pdf, Jpeg, Png], FiveMegabytes, IsPublic: false);
    private static readonly FilePolicy CsvFiles = new([Csv], FiveMegabytes, IsPublic: false);

    public static FilePolicy For(FilePurpose purpose) => purpose switch
    {
        FilePurpose.VerificationDocument => Documents,
        FilePurpose.Logo or FilePurpose.Banner or FilePurpose.CatalogImage or FilePurpose.GiftImage => Images,
        FilePurpose.CouponCsv or FilePurpose.RecipientCsv => CsvFiles,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null),
    };
}
