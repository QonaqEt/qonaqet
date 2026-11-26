using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Qonaqet.Application.Abstractions.Services;

namespace Qonaqet.Infrastructure.Identity;

/// <summary>
/// Serverdə geri açıla bilən sirlər (ADR 0008): AES-256-GCM, açar = HMAC-SHA256(<c>Security:GiftLinkSealSecret</c>, məqsəd) — eyni konfiqurasiya
/// sirri ilə hər məqsədin öz açarı olur. Format: base64url(nonce ‖ tag ‖ şifrəli mətn).
/// </summary>
public sealed class AesSecretSealer(IOptions<SecurityOptions> options, IHostEnvironment environment) : ISecretSealer
{
    private const int NonceSize = 12;

    private const int TagSize = 16;

    private readonly Lazy<byte[]> _root = new(() =>
        DevelopmentSecrets.Resolve(options.Value.GiftLinkSealSecret, environment, "Security:GiftLinkSealSecret"));

    public string Seal(string purpose, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var plain = Encoding.UTF8.GetBytes(value);
        var output = new byte[NonceSize + TagSize + plain.Length];
        RandomNumberGenerator.Fill(output.AsSpan(0, NonceSize));
        using var aes = new AesGcm(Key(purpose), TagSize);
        aes.Encrypt(output.AsSpan(0, NonceSize), plain, output.AsSpan(NonceSize + TagSize), output.AsSpan(NonceSize, TagSize));
        return SecureRandom.Base64Url(output);
    }

    public string? Unseal(string purpose, string sealedValue)
    {
        if (string.IsNullOrWhiteSpace(sealedValue))
        {
            return null;
        }

        try
        {
            var padded = sealedValue.Replace('-', '+').Replace('_', '/');
            padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
            var data = Convert.FromBase64String(padded);
            if (data.Length <= NonceSize + TagSize)
            {
                return null;
            }

            var plain = new byte[data.Length - NonceSize - TagSize];
            using var aes = new AesGcm(Key(purpose), TagSize);
            aes.Decrypt(data.AsSpan(0, NonceSize), data.AsSpan(NonceSize + TagSize), data.AsSpan(NonceSize, TagSize), plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }

    private byte[] Key(string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        return HMACSHA256.HashData(_root.Value, Encoding.UTF8.GetBytes("seal:" + purpose));
    }
}
