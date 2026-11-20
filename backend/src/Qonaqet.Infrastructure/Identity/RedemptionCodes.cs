using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Qonaqet.Application.Abstractions.Services;

namespace Qonaqet.Infrastructure.Identity;

/// <summary>
/// <see cref="IRedemptionCodes"/>: HMAC-SHA256 (öz açarı, <c>Security:QrSigningSecret</c>). QR imzası 128 bitə qısaldılır ki, QR
/// kiçik (oxunaqlı) qalsın; qısa kod imzanın ilk 40 bitidir. Açar dəyişərsə köhnə kodlar etibarsız olur.
/// </summary>
public sealed class RedemptionCodes(IOptions<SecurityOptions> options, IHostEnvironment environment) : IRedemptionCodes
{
    public const int ShortCodeLength = 8;

    // Crockford base32: 0/O, 1/I/L qarışmır, U yoxdur.
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int SignatureBytes = 16;

    private readonly Lazy<byte[]> _key = new(() =>
        DevelopmentSecrets.Resolve(options.Value.QrSigningSecret, environment, "Security:QrSigningSecret"));

    public string QrPayload(RedemptionCodeKind kind, Guid id)
    {
        var body = $"{Prefix(kind)}.{id:N}";
        return SecureRandom.Base64Url(Encoding.ASCII.GetBytes($"{body}.{SecureRandom.Base64Url(Sign(body)[..SignatureBytes])}"));
    }

    public RedemptionCodeRef? ParseQr(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload) || payload.Length > 200 || FromBase64Url(payload.Trim()) is not { } raw)
        {
            return null;
        }

        var parts = raw.Split('.');
        if (parts.Length != 3 || KindOf(parts[0]) is not { } kind || !Guid.TryParseExact(parts[1], "N", out var id)
            || FromBase64UrlBytes(parts[2]) is not { Length: SignatureBytes } signature)
        {
            return null;
        }

        var expected = Sign($"{parts[0]}.{parts[1]}")[..SignatureBytes];
        return CryptographicOperations.FixedTimeEquals(expected, signature) ? new RedemptionCodeRef(kind, id) : null;
    }

    public string ShortCode(RedemptionCodeKind kind, Guid id)
    {
        var mac = Sign($"s.{Prefix(kind)}.{id:N}");
        ulong bits = 0;
        for (var i = 0; i < 5; i++)
        {
            bits = (bits << 8) | mac[i];
        }

        var chars = new char[ShortCodeLength];
        for (var i = ShortCodeLength - 1; i >= 0; i--)
        {
            chars[i] = Alphabet[(int)(bits & 31)];
            bits >>= 5;
        }

        return new string(chars);
    }

    public string? ShortCodeHash(string? input)
    {
        if (Normalize(input) is not { } code)
        {
            return null;
        }

        return Convert.ToHexStringLower(Sign($"short:{code}"));
    }

    /// <summary>Böyük hərf, defis/boşluq atılır, O→0, I/L→1; uzunluq və əlifba yoxlanılır.</summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 20)
        {
            return null;
        }

        var builder = new StringBuilder(ShortCodeLength);
        foreach (var c in input.Trim().ToUpperInvariant())
        {
            var mapped = c switch
            {
                '-' or ' ' => '\0',
                'O' => '0',
                'I' or 'L' => '1',
                _ => c,
            };
            if (mapped == '\0')
            {
                continue;
            }

            if (!Alphabet.Contains(mapped, StringComparison.Ordinal))
            {
                return null;
            }

            builder.Append(mapped);
        }

        return builder.Length == ShortCodeLength ? builder.ToString() : null;
    }

    private byte[] Sign(string value) => HMACSHA256.HashData(_key.Value, Encoding.ASCII.GetBytes(value));

    private static string Prefix(RedemptionCodeKind kind) => kind switch
    {
        RedemptionCodeKind.Claim => "c",
        RedemptionCodeKind.Gift => "g",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static RedemptionCodeKind? KindOf(string prefix) => prefix switch
    {
        "c" => RedemptionCodeKind.Claim,
        "g" => RedemptionCodeKind.Gift,
        _ => null,
    };

    private static string? FromBase64Url(string value) =>
        FromBase64UrlBytes(value) is { } bytes && bytes.All(b => b is >= 0x21 and < 0x7F) ? Encoding.ASCII.GetString(bytes) : null;

    private static byte[]? FromBase64UrlBytes(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", 0 => string.Empty, _ => "!" };
        var buffer = new byte[padded.Length];
        return Convert.TryFromBase64String(padded, buffer, out var written) ? buffer[..written] : null;
    }
}
