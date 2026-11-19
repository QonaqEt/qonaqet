using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Domain.Users;
using Qonaqet.Infrastructure.Web;

namespace Qonaqet.Infrastructure.Identity;

/// <summary><c>Jwt</c> bölməsi. İmza açarı yalnız konfiqurasiya/mühitdən gəlir (<c>Jwt__SigningKey</c>, ən azı 32 bayt).</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "qonaqet";

    public string Audience { get; set; } = "qonaqet-app";

    public string? SigningKey { get; set; }

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);
}

/// <summary>HS256 açarı: Development-də verilməyibsə dev açarı, digər mühitlərdə məcburi.</summary>
public sealed class JwtSigningKey(IOptions<JwtOptions> options, IHostEnvironment environment)
{
    private readonly Lazy<SymmetricSecurityKey> _key = new(() =>
    {
        var bytes = DevelopmentSecrets.Resolve(options.Value.SigningKey, environment, "Jwt:SigningKey");
        if (bytes.Length < 32)
        {
            throw new InvalidOperationException("Jwt:SigningKey ən azı 32 bayt olmalıdır.");
        }

        return new SymmetricSecurityKey(bytes);
    });

    public SymmetricSecurityKey Key => _key.Value;
}

/// <summary>Access token: <c>sub</c> = istifadəçi ID-si, hər rol üçün bir <c>role</c> claim-i. Şəxsi məlumat (email, ad) yoxdur.</summary>
public sealed class JwtAccessTokenIssuer(IOptions<JwtOptions> options, JwtSigningKey signingKey) : IAccessTokenIssuer
{
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken Issue(User user, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(user);
        var settings = options.Value;
        var expires = now + settings.AccessTokenLifetime;

        var claims = new Dictionary<string, object>
        {
            [QonaqetClaims.Subject] = user.Id.Value.ToString("D"),
            [JwtRegisteredClaimNames.Jti] = Guid.CreateVersion7().ToString("N"),
            [QonaqetClaims.Role] = user.Roles.Select(r => r.ToString()).ToArray(),
        };
        if (user.BusinessId is { } businessId)
        {
            claims[QonaqetClaims.BusinessId] = businessId.ToString("D");
        }

        if (user.CorporateId is { } corporateId)
        {
            claims[QonaqetClaims.CorporateId] = corporateId.ToString("D");
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(signingKey.Key, SecurityAlgorithms.HmacSha256),
        };

        return new AccessToken(_handler.CreateToken(descriptor), expires);
    }
}

public static class JwtRegistration
{
    /// <summary>İstifadəçi JWT-ləri (Api host-u). Token yoxdursa və ya etibarsızdırsa aktor anonimdir; 401/403-ü pipeline qaytarır.</summary>
    public static IServiceCollection AddQonaqetUserAuthentication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearer>();
        return services;
    }

    private sealed class ConfigureJwtBearer(IOptions<JwtOptions> options, JwtSigningKey signingKey) : IConfigureNamedOptions<JwtBearerOptions>
    {
        public void Configure(JwtBearerOptions jwt) => Configure(JwtBearerDefaults.AuthenticationScheme, jwt);

        public void Configure(string? name, JwtBearerOptions jwt)
        {
            ArgumentNullException.ThrowIfNull(jwt);
            var settings = options.Value;
            jwt.MapInboundClaims = false;
            jwt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = settings.Issuer,
                ValidAudience = settings.Audience,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                NameClaimType = QonaqetClaims.Subject,
                RoleClaimType = QonaqetClaims.Role,
                ClockSkew = TimeSpan.FromSeconds(30),

                // Açar yalnız token gələndə həll olunur: açarsız (məs. açarsız Production testi) host yenə qalxır.
                IssuerSigningKeyResolver = (_, _, _, _) => [signingKey.Key],
            };
        }
    }
}
