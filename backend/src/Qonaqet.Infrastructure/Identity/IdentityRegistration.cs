using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Auth.Common;
using Qonaqet.Infrastructure.Persistence.Mongo.Repositories;

namespace Qonaqet.Infrastructure.Identity;

/// <summary>Hesab, OTP və token xidmətlərinin qeydiyyatı (bütün host-lar).</summary>
public static class IdentityRegistration
{
    public static IServiceCollection AddIdentityServices(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.SectionName));
        services.AddSingleton<ISecretHasher, HmacSecretHasher>();
        services.AddSingleton<ISecureRandom, SecureRandom>();
        services.AddSingleton<IOtpSender, LoggingOtpSender>();
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddSingleton<JwtSigningKey>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddScoped<SessionIssuer>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IOtpCodeRepository, OtpCodeRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        return services;
    }
}
