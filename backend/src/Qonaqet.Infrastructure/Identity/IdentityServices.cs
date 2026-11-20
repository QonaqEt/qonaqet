using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qonaqet.Application.Abstractions.Services;

namespace Qonaqet.Infrastructure.Identity;

/// <summary><c>Security</c> bölməsi: server sirləri yalnız konfiqurasiya/mühitdən gəlir.</summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>OTP kodlarının və refresh token-lərin HMAC açarı (<c>Security__TokenHashSecret</c>).</summary>
    public string? TokenHashSecret { get; set; }

    /// <summary>FIN hash-inin ayrıca HMAC açarı (<c>Security__FinHashSecret</c>); token açarı ilə paylaşılmır.</summary>
    public string? FinHashSecret { get; set; }

    /// <summary>Təhvil kodlarının (claim/hədiyyə QR imzası və qısa kod) ayrıca HMAC açarı (<c>Security__QrSigningSecret</c>).</summary>
    public string? QrSigningSecret { get; set; }

    /// <summary>Hədiyyə link token-inin müvəqqəti şifrəli nüsxəsinin açarı (<c>Security__GiftLinkSealSecret</c>, AES-256-GCM, ADR 0007).</summary>
    public string? GiftLinkSealSecret { get; set; }
}

/// <summary>
/// Development-də sirr verilməyibsə sabit dev açarı işlədilir (yerli işə salma asan olsun deyə);
/// digər mühitlərdə sirr olmadan ilk istifadədə xəta atılır.
/// </summary>
internal static class DevelopmentSecrets
{
    public static byte[] Resolve(string? configured, IHostEnvironment environment, string settingName)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Encoding.UTF8.GetBytes(configured);
        }

        if (environment.IsDevelopment())
        {
            return Encoding.UTF8.GetBytes($"qonaqet-dev-only::{settingName}::not-a-secret-0123456789");
        }

        throw new InvalidOperationException($"{settingName} konfiqurasiya olunmayıb.");
    }
}

public sealed class HmacSecretHasher(IOptions<SecurityOptions> options, IHostEnvironment environment) : ISecretHasher
{
    private readonly Lazy<byte[]> _key = new(() =>
        DevelopmentSecrets.Resolve(options.Value.TokenHashSecret, environment, "Security:TokenHashSecret"));

    public string Hash(string purpose, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(purpose);
        ArgumentNullException.ThrowIfNull(value);
        var mac = HMACSHA256.HashData(_key.Value, Encoding.UTF8.GetBytes($"{purpose}:{value}"));
        return Convert.ToHexStringLower(mac);
    }

    public bool Verify(string purpose, string value, string hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        var expected = Encoding.ASCII.GetBytes(Hash(purpose, value));
        return CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(hash));
    }
}

/// <summary>
/// FIN → <c>HMAC-SHA256(FinHashSecret, UPPER(FIN))</c>, kiçik hərfli hex. Xam FIN heç yerdə saxlanılmır; eyni FIN eyni hash verir,
/// ona görə bir FIN bir kateqoriyada yalnız bir hesaba bağlana bilir.
/// </summary>
public sealed class HmacFinHasher(IOptions<SecurityOptions> options, IHostEnvironment environment) : IFinHasher
{
    private readonly Lazy<byte[]> _key = new(() =>
        DevelopmentSecrets.Resolve(options.Value.FinHashSecret, environment, "Security:FinHashSecret"));

    public string Hash(string fin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fin);
        var mac = HMACSHA256.HashData(_key.Value, Encoding.UTF8.GetBytes(fin.Trim().ToUpperInvariant()));
        return Convert.ToHexStringLower(mac);
    }
}

public sealed class SecureRandom : ISecureRandom
{
    public string NumericCode(int digits)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(digits, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(digits, 9);
        var max = (int)Math.Pow(10, digits);
        return RandomNumberGenerator.GetInt32(0, max).ToString($"D{digits}", System.Globalization.CultureInfo.InvariantCulture);
    }

    public string Token(int bytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bytes, 16);
        return Base64Url(RandomNumberGenerator.GetBytes(bytes));
    }

    internal static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
