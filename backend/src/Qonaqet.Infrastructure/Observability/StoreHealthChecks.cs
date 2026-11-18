using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Infrastructure.Caching;
using Qonaqet.Infrastructure.Messaging.RabbitMq;
using Qonaqet.Infrastructure.Persistence.Postgres;

namespace Qonaqet.Infrastructure.Observability;

/// <summary>Readiness yoxlamaları: hər store qısa timeout ilə ping olunur.</summary>
public static class StoreHealthChecks
{
    public const string ReadyTag = "ready";

    public static IHealthChecksBuilder AddStoreHealthChecks(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var timeout = TimeSpan.FromSeconds(3);
        return services.AddHealthChecks()
            .AddCheck<MongoHealthCheck>("mongo", tags: [ReadyTag], timeout: timeout)
            .AddCheck<PostgresHealthCheck>("postgres", tags: [ReadyTag], timeout: timeout)
            .AddCheck<ValkeyHealthCheck>("valkey", failureStatus: HealthStatus.Degraded, tags: [ReadyTag], timeout: timeout)
            .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: [ReadyTag], timeout: timeout);
    }
}

public sealed class MongoHealthCheck(IMongoDatabase database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Mongo əlçatan deyil", ex);
        }
    }
}

public sealed class PostgresHealthCheck(QonaqetDbContext context) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext healthContext, CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Postgres əlçatan deyil");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Postgres əlçatan deyil", ex);
        }
    }
}

/// <summary>Valkey yalnız keşdir: əlçatmaz olanda sistem işləyir, ona görə nəticə <c>Degraded</c>-dir.</summary>
public sealed class ValkeyHealthCheck(ValkeyConnection connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = await connection.DatabaseAsync().ConfigureAwait(false);
            await db.PingAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new HealthCheckResult(context?.Registration.FailureStatus ?? HealthStatus.Degraded, "Valkey əlçatan deyil", ex);
        }
    }
}

public sealed class RabbitMqHealthCheck(RabbitMqConnection connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var conn = await connection.GetAsync(cancellationToken).ConfigureAwait(false);
            return conn.IsOpen ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("RabbitMQ bağlantısı bağlıdır");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ əlçatan deyil", ex);
        }
    }
}
