using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Infrastructure.Persistence.Postgres.Interceptors;

namespace Qonaqet.Infrastructure.Persistence.Postgres;

/// <summary>Postgres store-unun DI qeydiyyatı (<c>ConnectionStrings:Postgres</c>).</summary>
public static class PostgresRegistration
{
    public const string ConnectionStringName = "Postgres";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public static IServiceCollection AddPostgres(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName) ?? "Host=localhost;Database=qonaqet";

        services.AddScoped<SoftDeleteInterceptor>();
        services.AddScoped<AuditInterceptor>();
        services.AddScoped<OutboxInterceptor>();
        services.AddDbContext<QonaqetDbContext>((sp, options) => Configure(options, connectionString)
            .AddInterceptors(
                sp.GetRequiredService<SoftDeleteInterceptor>(),
                sp.GetRequiredService<AuditInterceptor>(),
                sp.GetRequiredService<OutboxInterceptor>()));

        // Default IUnitOfWork/IOutbox Mongo-dur; Postgres əmrləri bunları konkret tip kimi alır.
        services.AddScoped<PostgresUnitOfWork>();
        services.AddScoped<PostgresOutbox>();
        services.AddScoped<IIdempotencyStore, PostgresIdempotencyStore>();

        services.AddHostedService<PostgresMigrationHostedService>();
        return services;
    }

    /// <summary>Provayder parametrləri (host-lar və testlər eyni konfiqurasiyanı işlədir).</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);
        // Migrasiyalar əl ilə yazılır (ADR 0006: Microsoft.EntityFrameworkCore.Design allowlist-də deyil),
        // model snapshot-u yoxdur. Sxemin modelə uyğunluğunu integration testi yoxlayır.
        return options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, QonaqetDbContext.Schema))
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
    }
}

/// <summary><c>Postgres:ApplyMigrationsOnStartup = true</c> olduqda EF migrasiyalarını tətbiq edir.</summary>
public sealed class PostgresMigrationHostedService(IServiceScopeFactory scopes, IConfiguration configuration) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Postgres:ApplyMigrationsOnStartup"))
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
