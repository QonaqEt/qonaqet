using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Qonaqet.Application;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Infrastructure.Caching;
using Qonaqet.Infrastructure.Documents.Csv;
using Qonaqet.Infrastructure.Documents.Qr;
using Qonaqet.Infrastructure.Email;
using Qonaqet.Infrastructure.Identity;
using Qonaqet.Infrastructure.Storage;
using Qonaqet.Infrastructure.Web;
using Qonaqet.Infrastructure.Persistence;
using Qonaqet.Infrastructure.Persistence.Mongo;
using Qonaqet.Infrastructure.Persistence.Postgres;
using Qonaqet.SharedKernel;
using Qonaqet.Infrastructure.Messaging;
using Qonaqet.Infrastructure.Messaging.RabbitMq;

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
        services.AddPostgres(configuration);
        services.AddValkey(configuration);
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.AddSingleton<RabbitMqConnection>();

        services.AddFileStorage(configuration);
        services.AddEmail(configuration);
        services.AddIdentityServices(configuration);

        // HTTP host-ları (AddQonaqetWeb) öz aktorlarını əvvəlcədən qeyd edir; Worker anonim aktor alır.
        services.TryAddSingleton<AnonymousActor>();
        services.TryAddScoped<ICurrentUser>(sp => sp.GetRequiredService<AnonymousActor>());
        services.TryAddScoped<IRequestContext>(sp => sp.GetRequiredService<AnonymousActor>());
        return services;
    }
}
