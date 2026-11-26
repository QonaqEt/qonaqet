using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Domain.Integration;
using Qonaqet.Infrastructure.Web;

namespace Qonaqet.Infrastructure.Identity;

/// <summary>Partner token claim-ləri (şəxsi məlumat yoxdur).</summary>
public static class PartnerClaims
{
    public const string Audience = "qonaqet-partner";

    public const string ClientId = "client_id";

    public const string Sandbox = "sandbox";

    /// <summary>Client-in dəqiqəlik limiti (rate limiter partition-u üçün).</summary>
    public const string RateLimit = "rate_limit";

    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);
}

/// <summary>
/// Partner access token: <c>sub</c> = client-in daxili ID-si, rollar = <c>PartnerClient</c> + verilmiş scope-lar. İmza açarı istifadəçi JWT-ləri
/// ilə eynidir, amma audience fərqlidir — bir host-un token-i digərində qəbul olunmur.
/// </summary>
public sealed class JwtPartnerTokenIssuer(IOptions<JwtOptions> options, JwtSigningKey signingKey) : IPartnerTokenIssuer
{
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken Issue(IntegrationClient client, IReadOnlyList<string> scopes, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(scopes);
        var expires = now + PartnerClaims.Lifetime;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Value.Issuer,
            Audience = PartnerClaims.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [QonaqetClaims.Subject] = client.Id.Value.ToString("D"),
                [JwtRegisteredClaimNames.Jti] = Guid.CreateVersion7().ToString("N"),
                [QonaqetClaims.Role] = new[] { PartnerScopes.ClientRole }.Concat(scopes).ToArray(),
                [PartnerClaims.ClientId] = client.ClientId,
                [PartnerClaims.Sandbox] = client.IsSandbox,
                [PartnerClaims.RateLimit] = client.RateLimitPerMinute,
            },
            SigningCredentials = new SigningCredentials(signingKey.Key, SecurityAlgorithms.HmacSha256),
        };

        return new AccessToken(_handler.CreateToken(descriptor), expires);
    }
}

public static class PartnerJwtRegistration
{
    /// <summary>Partner JWT-ləri (Integration.Api host-u): yalnız <see cref="PartnerClaims.Audience"/> audience-li token qəbul olunur.</summary>
    public static IServiceCollection AddQonaqetPartnerAuthentication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigurePartnerJwtBearer>();
        return services;
    }

    private sealed class ConfigurePartnerJwtBearer(IOptions<JwtOptions> options, JwtSigningKey signingKey) : IConfigureNamedOptions<JwtBearerOptions>
    {
        public void Configure(JwtBearerOptions jwt) => Configure(JwtBearerDefaults.AuthenticationScheme, jwt);

        public void Configure(string? name, JwtBearerOptions jwt)
        {
            ArgumentNullException.ThrowIfNull(jwt);
            jwt.MapInboundClaims = false;
            jwt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = options.Value.Issuer,
                ValidAudience = PartnerClaims.Audience,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                NameClaimType = QonaqetClaims.Subject,
                RoleClaimType = QonaqetClaims.Role,
                ClockSkew = TimeSpan.FromSeconds(30),
                IssuerSigningKeyResolver = (_, _, _, _) => [signingKey.Key],
            };
        }
    }
}
