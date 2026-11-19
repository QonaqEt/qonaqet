using Qonaqet.Domain.Categories;
using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Verification;

public readonly record struct VerificationRequestId(Guid Value) : IStronglyTypedId<VerificationRequestId>
{
    public static VerificationRequestId From(Guid value) => new(value);
}

public enum VerificationStatus
{
    Pending,
    Approved,
    Rejected,
}

/// <summary>
/// Kateqoriya təsdiq sorğusu (<c>verificationRequests</c>). Cavablar kateqoriyanın dinamik konfiqurasiyasına görə yoxlanılır;
/// xam FIN heç vaxt saxlanılmır (yalnız <see cref="FinHash"/>). Sənədlər <c>files</c>-dadır və yalnız sahibi və admin görür.
/// </summary>
public sealed class VerificationRequest : AggregateRoot<VerificationRequestId>
{
    private Dictionary<string, string> _fields = [];
    private List<Guid> _fileIds = [];

    private VerificationRequest(
        VerificationRequestId id, Guid userId, CategoryId categoryId, string? subtype, Dictionary<string, string> fields, string? finHash,
        List<Guid> fileIds, DateTimeOffset createdAt)
        : base(id, createdAt, userId)
    {
        UserId = userId;
        CategoryId = categoryId;
        Subtype = subtype;
        _fields = fields;
        FinHash = finHash;
        _fileIds = fileIds;
        Status = VerificationStatus.Pending;
    }

    private VerificationRequest()
    {
    }

    public Guid UserId { get; private set; }

    public CategoryId CategoryId { get; private set; }

    public string? Subtype { get; private set; }

    /// <summary>Dinamik sahələrin cavabları (açar → dəyər).</summary>
    public IReadOnlyDictionary<string, string> Fields
    {
        get => _fields;
        private set => _fields = new Dictionary<string, string>(value, StringComparer.Ordinal);
    }

    /// <summary>FIN-in HMAC-SHA256 hash-i (kateqoriya FIN tələb edirsə).</summary>
    public string? FinHash { get; private set; }

    public IReadOnlyList<Guid> FileIds
    {
        get => _fileIds;
        private set => _fileIds = [.. value];
    }

    public VerificationStatus Status { get; private set; }

    public string? RejectReason { get; private set; }

    public Guid? ReviewedBy { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    public bool IsPending => Status == VerificationStatus.Pending;

    /// <summary>Yeni ID: sənədlər sorğu yaradılmamışdan əvvəl bu ID-nin sahibliyi ilə yüklənir.</summary>
    public static VerificationRequestId NewId() => StronglyTypedId.New<VerificationRequestId>();

    public static VerificationRequest Submit(
        VerificationRequestId id, Guid userId, CategoryId categoryId, string? subtype, IReadOnlyDictionary<string, string> fields, string? finHash,
        IReadOnlyList<Guid> fileIds, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(fileIds);
        var request = new VerificationRequest(
            id, Guard.NotEmpty(userId), categoryId, string.IsNullOrWhiteSpace(subtype) ? null : subtype, new Dictionary<string, string>(fields, StringComparer.Ordinal),
            finHash, [.. fileIds], at);
        request.Raise(new VerificationSubmitted(id, categoryId, userId, at));
        return request;
    }
}

/// <summary>Təsdiq sorğusu göndərildi (admin növbəsi).</summary>
public sealed record VerificationSubmitted(VerificationRequestId RequestId, CategoryId CategoryId, Guid UserId, DateTimeOffset At) : DomainEvent(At);
