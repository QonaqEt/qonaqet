using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Domain.Users;

namespace Qonaqet.Application.Features.Auth.Common;

/// <summary>Klientə verilən token cütü. Refresh token yalnız bu cavabda açıq görünür.</summary>
public sealed record AuthTokensDto(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

/// <summary>Yeni sessiya: access token + hash-i saxlanılan refresh token (OTP girişi və rotasiya üçün ortaq).</summary>
public sealed class SessionIssuer(IRefreshTokenRepository refreshTokens, IAccessTokenIssuer accessTokens, ISecretHasher hasher, ISecureRandom random)
{
    public const string HashPurpose = "refresh";

    public const int TokenBytes = 32;

    public async Task<(AuthTokensDto Tokens, RefreshTokenId RefreshTokenId)> IssueAsync(User user, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var raw = random.Token(TokenBytes);
        var refresh = RefreshToken.Issue(user.Id, hasher.Hash(HashPurpose, raw), now);
        await refreshTokens.AddAsync(refresh, cancellationToken).ConfigureAwait(false);

        var access = accessTokens.Issue(user, now);
        return (new AuthTokensDto(access.Token, access.ExpiresAt, raw, refresh.ExpiresAt), refresh.Id);
    }

    public string Hash(string rawRefreshToken) => hasher.Hash(HashPurpose, rawRefreshToken);
}
