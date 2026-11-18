using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Application.Abstractions.Services;

namespace Qonaqet.Infrastructure.Caching;

/// <summary>Valkey (Redis-uyğun) xidmətlərinin DI qeydiyyatı.</summary>
public static class ValkeyRegistration
{
    public static IServiceCollection AddValkey(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ValkeyOptions>(configuration.GetSection(ValkeyOptions.SectionName));
        services.AddSingleton<ValkeyConnection>();
        services.AddSingleton<ICache, ValkeyCache>();
        services.AddSingleton<IDistributedLock, ValkeyDistributedLock>();
        services.AddSingleton<IRateLimitStore, ValkeyRateLimitStore>();
        return services;
    }
}
