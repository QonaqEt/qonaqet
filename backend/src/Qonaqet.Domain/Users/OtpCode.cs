using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Users;

/// <summary>OTP qaydaları (<c>CLAUDE.md</c> → <c>otpCodes</c>).</summary>
public static class OtpPolicy
{
    public const int Digits = 6;

    public const int MaxAttempts = 5;

    public const int MaxSendsPerWindow = 3;

    public static readonly TimeSpan Validity = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan SendWindow = TimeSpan.FromMinutes(15);
}

/// <summary>
/// Email-ə göndərilmiş birdəfəlik kod (<c>otpCodes</c>). Yalnız <see cref="CodeHash"/> saxlanılır, açıq kod heç vaxt.
/// Müddət məntiqidir: <see cref="ExpiresAt"/> indiki vaxtla müqayisə olunur (TTL indeksi yoxdur).
/// </summary>
public sealed class OtpCode : AggregateRoot<OtpCodeId>
{
    private OtpCode(OtpCodeId id, string email, string codeHash, DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        Email = email;
        CodeHash = codeHash;
        ExpiresAt = createdAt + OtpPolicy.Validity;
    }

    private OtpCode()
    {
    }

    public string Email { get; private set; } = string.Empty;

    public string CodeHash { get; private set; } = string.Empty;

    public int Attempts { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public static OtpCode Issue(string email, string codeHash, DateTimeOffset at) =>
        new(StronglyTypedId.New<OtpCodeId>(), EmailAddress.Normalize(email), Guard.NotNullOrWhiteSpace(codeHash), at);

    /// <summary>Hələ istifadə olunmayıb, vaxtı keçməyib və cəhd limiti dolmayıb.</summary>
    public bool IsUsable(DateTimeOffset now) => UsedAt is null && ExpiresAt > now && Attempts < OtpPolicy.MaxAttempts;
}
