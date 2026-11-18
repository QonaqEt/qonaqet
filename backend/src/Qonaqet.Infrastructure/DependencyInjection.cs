using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Application;
using Qonaqet.Infrastructure.Messaging;

namespace Qonaqet.Infrastructure;

/// <summary>Bütün host-ların (Api, Integration.Api, Worker) paylaşdığı infrastruktur qeydiyyatı.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddMessaging(typeof(ApplicationAssembly).Assembly);

        // Store-lar, outbox, cache və scheduler sonrakı E01 task-ları ilə əlavə olunur.
        return services;
    }
}
