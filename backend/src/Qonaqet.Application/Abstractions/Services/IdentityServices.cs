namespace Qonaqet.Application.Abstractions.Services;

/// <summary>
/// Gizli dəyərlərin (OTP kodu, refresh token) server sirri ilə HMAC-SHA256 hash-i. Açıq dəyər heç vaxt saxlanılmır.
/// <c>purpose</c> fərqli kontekstlərin hash-lərini bir-birindən ayırır.
/// </summary>
public interface ISecretHasher
{
    string Hash(string purpose, string value);

    /// <summary>Sabit vaxtlı müqayisə.</summary>
    bool Verify(string purpose, string value, string hash);
}

/// <summary>Kriptoqrafik təsadüfi dəyərlər.</summary>
public interface ISecureRandom
{
    /// <summary><paramref name="digits"/> rəqəmli kod (aparıcı sıfırlarla).</summary>
    string NumericCode(int digits);

    /// <summary><paramref name="bytes"/> təsadüfi bayt, base64url.</summary>
    string Token(int bytes);
}

/// <summary>OTP kodunu istifadəçiyə çatdırır (email). Kod yalnız Development-də loglanır.</summary>
public interface IOtpSender
{
    Task SendAsync(string email, string code, DateTimeOffset expiresAt, CancellationToken cancellationToken);
}
