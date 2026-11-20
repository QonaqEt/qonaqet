using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Domain.Gifts;

namespace Qonaqet.Infrastructure.Identity;

/// <summary>
/// <see cref="IGiftLinkTokens"/>: token 32 təsadüfi bayt (base64url, 43 simvol). Axtarış üçün <see cref="ISecretHasher"/> (HMAC, ayrıca
/// məqsəd adı ilə) hash-i; müvəqqəti nüsxə AES-256-GCM ilə (açar <c>Security:GiftLinkSealSecret</c>-in SHA-256-sı), formatı
/// <c>base64url(nonce ‖ tag ‖ şifrəli mətn)</c>. Açar dəyişərsə hələ çatdırılmamış linklər yenidən verilməlidir (reissue).
/// </summary>
public sealed class GiftLinkTokens(ISecretHasher hasher, ISecureRandom random, IOptions<SecurityOptions> options, IHostEnvironment environment)
    : IGiftLinkTokens
{
    public const int TokenBytes = 32;
    public const int TokenLength = 43;
    private const string HashPurpose = "gift-link";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly Lazy<byte[]> _key = new(() =>
        SHA256.HashData(DevelopmentSecrets.Resolve(options.Value.GiftLinkSealSecret, environment, "Security:GiftLinkSealSecret")));

    public GiftLinkIssue Issue()
    {
        var token = random.Token(TokenBytes);
        return new GiftLinkIssue(token, new GiftLinkSecret(hasher.Hash(HashPurpose, token), Seal(token)));
    }

    public string? Hash(string? token) => IsWellFormed(token) ? hasher.Hash(HashPurpose, token!) : null;

    public string? Unseal(string? sealedToken)
    {
        if (string.IsNullOrWhiteSpace(sealedToken))
        {
            return null;
        }

        try
        {
            var data = FromBase64Url(sealedToken);
            if (data.Length <= NonceSize + TagSize)
            {
                return null;
            }

            var plain = new byte[data.Length - NonceSize - TagSize];
            using var aes = new AesGcm(_key.Value, TagSize);
            aes.Decrypt(data.AsSpan(0, NonceSize), data.AsSpan(NonceSize + TagSize), data.AsSpan(NonceSize, TagSize), plain);
            var token = Encoding.ASCII.GetString(plain);
            return IsWellFormed(token) ? token : null;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }

    /// <summary>Base64url əlifbası və 43 simvol (32 bayt).</summary>
    public static bool IsWellFormed(string? token) =>
        token is { Length: TokenLength } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private string Seal(string token)
    {
        var plain = Encoding.ASCII.GetBytes(token);
        var output = new byte[NonceSize + TagSize + plain.Length];
        RandomNumberGenerator.Fill(output.AsSpan(0, NonceSize));
        using var aes = new AesGcm(_key.Value, TagSize);
        aes.Encrypt(output.AsSpan(0, NonceSize), plain, output.AsSpan(NonceSize + TagSize), output.AsSpan(NonceSize, TagSize));
        return SecureRandom.Base64Url(output);
    }

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        return Convert.FromBase64String(padded);
    }
}
