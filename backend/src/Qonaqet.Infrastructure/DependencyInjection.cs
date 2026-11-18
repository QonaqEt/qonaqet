using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Application;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Infrastructure.Documents.Csv;
using Qonaqet.Infrastructure.Documents.Qr;
using Qonaqet.Infrastructure.Persistence;
using Qonaqet.Infrastructure.Persistence.Mongo;
using Qonaqet.SharedKernel;
using Qonaqet.Infrastructure.Messaging;

namespace Qonaqet.Infrastructure;

/// <summary>Bütün host-ların (Api, Integration.Api, Worker) paylaşdığı infrastruktur qeydiyyatı.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IAggregateTracker, AggregateTracker>();
        services.AddSingleton<ICsvService, CsvService>();
        services.AddSingleton<IQrCodeEncoder, QrEncoder>();
        services.AddMessaging(typeof(ApplicationAssembly).Assembly);
        services.AddApplicationPipeline();
        services.AddMongo(configuration);

        // IIdempotencyStore Postgres ilə (E01-T08), ICurrentUser və
        // IRequestContext host auth-u ilə (E02) gəlir.

        // Store-lar, outbox, cache və scheduler sonrakı E01 task-ları ilə əlavə olunur.
        return services;
    }
}
