using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Integration;

public readonly record struct IntegrationClientId(Guid Value) : IStronglyTypedId<IntegrationClientId>
{
    public static IntegrationClientId From(Guid value) => new(value);
}

/// <summary>Client-in sahibi: biznes (POS / şəbəkə), şirkət və ya partnyor (issuer, brend).</summary>
public enum IntegrationOwnerType
{
    Business,
    Corporate,
    Partner,
}

/// <summary>Partner API scope-ları (OAuth2 <c>scope</c>).</summary>
public static class PartnerScopes
{
    /// <summary>Hər partner token-inin rolu (scope-lar ayrıca rol kimi əlavə olunur).</summary>
    public const string ClientRole = "PartnerClient";

    public const string CodesRead = "codes:read";
    public const string RedemptionsWrite = "redemptions:write";
    public const string CatalogWrite = "catalog:write";
    public const string GiftsIssue = "gifts:issue";
    public const string GiftsRead = "gifts:read";
    public const string CouponsValidate = "coupons:validate";
    public const string Webhooks = "webhooks";

    public static IReadOnlyList<string> All { get; } = [CodesRead, RedemptionsWrite, CatalogWrite, GiftsIssue, GiftsRead, CouponsValidate, Webhooks];

    public static bool IsKnown(string scope) => All.Contains(scope, StringComparer.Ordinal);
}

/// <summary>
/// Partnyor sistemi üçün OAuth2 client (Postgres <c>integration_clients</c>). Secret yalnız yaradılanda və rotasiyada bir dəfə göstərilir; burada
/// yalnız onun HMAC hash-i saxlanılır. Rotasiyada köhnə secret 24 saat etibarlı qalır ki, partnyor sistemini dayandırmadan dəyişə bilsin.
/// Ləğv = soft delete. Çoxbiznesli client (şəbəkə) <see cref="BusinessIds"/> ilə yalnız öz bizneslərinə müraciət edir.
/// </summary>
public sealed class IntegrationClient : AggregateRoot<IntegrationClientId>
{
    public const int NameMaxLength = 120;

    public const int MaxBusinesses = 200;

    public const int MinRateLimit = 10;

    public const int MaxRateLimit = 6000;

    public const int DefaultRateLimit = 120;

    public static readonly TimeSpan RotationOverlap = TimeSpan.FromHours(24);

    private List<Guid> _businessIds = [];

    private List<string> _scopes = [];

    private IntegrationClient(IntegrationClientId id, DateTimeOffset createdAt, Guid? createdBy)
        : base(id, createdAt, createdBy)
    {
    }

    private IntegrationClient()
    {
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>İctimai identifikator (<c>client_id</c>), unikal.</summary>
    public string ClientId { get; private set; } = string.Empty;

    public string SecretHash { get; private set; } = string.Empty;

    /// <summary>Rotasiyadan əvvəlki secret-in hash-i; <see cref="PreviousSecretExpiresAt"/>-ə qədər etibarlıdır.</summary>
    public string? PreviousSecretHash { get; private set; }

    public DateTimeOffset? PreviousSecretExpiresAt { get; private set; }

    public IntegrationOwnerType OwnerType { get; private set; }

    public Guid OwnerId { get; private set; }

    public IReadOnlyList<Guid> BusinessIds
    {
        get => _businessIds;
        private set => _businessIds = [.. value];
    }

    public IReadOnlyList<string> Scopes
    {
        get => _scopes;
        private set => _scopes = [.. value];
    }

    public bool IsSandbox { get; private set; }

    public int RateLimitPerMinute { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    public static IntegrationClient Create(
        string name,
        string clientId,
        string secretHash,
        IntegrationOwnerType ownerType,
        Guid ownerId,
        IReadOnlyCollection<Guid> businessIds,
        IReadOnlyCollection<string> scopes,
        bool isSandbox,
        int rateLimitPerMinute,
        DateTimeOffset at,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(businessIds);
        ArgumentNullException.ThrowIfNull(scopes);
        if (!IsValid(name, businessIds, scopes, rateLimitPerMinute))
        {
            throw new ArgumentException("Client sahələri düzgün deyil.", nameof(name));
        }

        return new IntegrationClient(StronglyTypedId.New<IntegrationClientId>(), Guard.Utc(at), by)
        {
            Name = name.Trim(),
            ClientId = Guard.NotNullOrWhiteSpace(clientId),
            SecretHash = Guard.NotNullOrWhiteSpace(secretHash),
            OwnerType = ownerType,
            OwnerId = Guard.NotEmpty(ownerId),
            _businessIds = [.. businessIds.Distinct()],
            _scopes = [.. scopes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            IsSandbox = isSandbox,
            RateLimitPerMinute = rateLimitPerMinute,
        };
    }

    public static bool IsValid(string? name, IReadOnlyCollection<Guid>? businessIds, IReadOnlyCollection<string>? scopes, int rateLimitPerMinute) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= NameMaxLength
        && businessIds is not null && businessIds.Count <= MaxBusinesses && !businessIds.Contains(Guid.Empty)
        && scopes is { Count: > 0 } && scopes.All(PartnerScopes.IsKnown)
        && rateLimitPerMinute is >= MinRateLimit and <= MaxRateLimit;

    /// <summary>Yeni secret; köhnəsi <see cref="RotationOverlap"/> ərzində də qəbul olunur.</summary>
    public void RotateSecret(string newSecretHash, DateTimeOffset at, Guid? by)
    {
        PreviousSecretHash = SecretHash;
        PreviousSecretExpiresAt = Guard.Utc(at) + RotationOverlap;
        SecretHash = Guard.NotNullOrWhiteSpace(newSecretHash);
        MarkUpdated(at, by);
    }

    /// <summary>Verilən hash-lərdən biri cari secret-ə və ya hələ müddəti bitməmiş əvvəlki secret-ə uyğundurmu (müqayisəni çağıran edir).</summary>
    public bool AcceptsPreviousSecretAt(DateTimeOffset now) => PreviousSecretHash is not null && PreviousSecretExpiresAt > now;

    /// <summary>İstənən scope-lar client-ə verilmiş scope-ların alt çoxluğudurmu; boş sorğu = hamısı.</summary>
    public IReadOnlyList<string>? GrantScopes(IReadOnlyCollection<string> requested)
    {
        ArgumentNullException.ThrowIfNull(requested);
        if (requested.Count == 0)
        {
            return _scopes;
        }

        return requested.All(_scopes.Contains) ? [.. requested.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)] : null;
    }

    public bool CanAccessBusiness(Guid businessId) => _businessIds.Contains(businessId);

    public void MarkUsed(DateTimeOffset at) => LastUsedAt = Guard.Utc(at);
}
