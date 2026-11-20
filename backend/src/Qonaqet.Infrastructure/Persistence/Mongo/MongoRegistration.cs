using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Infrastructure.Messaging.Outbox;
using Qonaqet.Infrastructure.Persistence.Mongo.Migrations;

namespace Qonaqet.Infrastructure.Persistence.Mongo;

/// <summary>Mongo store-unun DI qeydiyyatı. Klient tənbəldir: qoşulma ilk sorğuda baş verir.</summary>
public static class MongoRegistration
{
    public static IServiceCollection AddMongo(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        MongoConventions.Register();
        services.Configure<MongoOptions>(configuration.GetSection(MongoOptions.SectionName));

        services.AddSingleton<IMongoClient>(sp => new MongoClient(sp.GetRequiredService<IOptions<MongoOptions>>().Value.ConnectionString));
        services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(sp.GetRequiredService<IOptions<MongoOptions>>().Value.Database));
        services.AddScoped(sp => new MongoContext(sp.GetRequiredService<IMongoClient>(), sp.GetRequiredService<IMongoDatabase>()));

        // Default UnitOfWork / outbox Mongo-dur; Postgres əmrləri öz UnitOfWork-unu E01-T08 ilə alır.
        services.AddScoped<IUnitOfWork, MongoUnitOfWork>();
        services.AddScoped<IOutbox, MongoOutbox>();

        services.AddSingleton<MongoIndexRunner>();
        services.AddSingleton<MongoMigrator>();
        services.AddSingleton<Seed.CategorySeeder>();
        services.AddSingleton<Seed.CampaignSeeder>();
        services.AddHostedService<MongoMigrationHostedService>();
        return services;
    }
}
