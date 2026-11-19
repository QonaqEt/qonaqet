using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Users;

public readonly record struct RefreshTokenId(Guid Value) : IStronglyTypedId<RefreshTokenId>
{
    public static RefreshTokenId From(Guid value) => new(value);
}

/// <summary>Refresh token ləğv səbəbləri.</summary>
public enum RefreshTokenRevokeReason
{
    Rotated,
    Logout,
    ReuseDetected,
    AccountBlocked,
    AccountDeleted,
}

/// <summary>
/// Refresh token (<c>refreshTokens</c>). Açıq dəyər (32 təsadüfi bayt) yalnız klientə bir dəfə verilir, burada
/// yalnız HMAC hash-i saxlanılır. Hər istifadədə rotasiya olunur: köhnə token ləğv edilir, yenisi verilir.
/// Ləğv olunmuş token-in yenidən gəlməsi oğurluq əlamətidir və istifadəçinin bütün sessiyalarını bağlayır.
/// </summary>
public sealed class RefreshToken : AggregateRoot<RefreshTokenId>
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    private RefreshToken(RefreshTokenId id, UserId userId, string tokenHash, DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAt = createdAt + Lifetime;
    }

    private RefreshToken()
    {
    }

    public UserId UserId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public RefreshTokenRevokeReason? RevokeReason { get; private set; }

    /// <summary>Rotasiyada bu token-i əvəz edən yeni token.</summary>
    public RefreshTokenId? ReplacedById { get; private set; }

    public static RefreshToken Issue(UserId userId, string tokenHash, DateTimeOffset at) =>
        new(StronglyTypedId.New<RefreshTokenId>(), userId, Guard.NotNullOrWhiteSpace(tokenHash), at);

    public bool IsRevoked => RevokedAt is not null;

    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;
}
