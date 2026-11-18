using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Infrastructure.Persistence.Postgres;
using Qonaqet.Infrastructure.Persistence.Postgres.Interceptors;
using Qonaqet.IntegrationTests.Persistence.Mongo;
using Qonaqet.SharedKernel;

namespace Qonaqet.IntegrationTests.Persistence.Postgres;

/// <summary>
/// Postgres konteyneri (image <c>deploy/docker-compose.yml</c>-dəki ilə eyni). Hər test öz verilənlər
/// bazasını alır. Docker yoxdursa lokal testlər skip olunur, CI-da xəta verir.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string Image = "postgres:18.6-alpine";
    private const string Password = "test-only";

    private IContainer? _container;

    public string? UnavailableReason { get; private set; }

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new ContainerBuilder(Image)
                .WithEnvironment("POSTGRES_PASSWORD", Password)
                .WithPortBinding(5432, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("pg_isready", "-U", "postgres", "-h", "127.0.0.1"))
                .Build();
            await _container.StartAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (Environment.GetEnvironmentVariable("CI") != "true")
        {
            UnavailableReason = $"Docker əlçatan deyil: {ex.Message}";
        }
    }

    public void SkipIfUnavailable() => Assert.SkipWhen(UnavailableReason is not null, UnavailableReason ?? string.Empty);

    /// <summary>Yeni boş verilənlər bazası yaradır və onun connection string-ini qaytarır.</summary>
    public async Task<string> NewDatabaseAsync()
    {
        var name = "t_" + Guid.NewGuid().ToString("N")[..12];
        await using (var admin = new NpgsqlConnection(ConnectionString("postgres")))
        {
            await admin.OpenAsync().ConfigureAwait(false);
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
            await create.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        return ConnectionString(name);
    }

    public static TContext CreateContext<TContext>(string connectionString, Func<DbContextOptions, TContext> factory, IClock clock, ICurrentUser? user = null)
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder();
        PostgresRegistration.Configure(builder, connectionString)
            .AddInterceptors(new SoftDeleteInterceptor(clock, user), new AuditInterceptor(clock, user), new OutboxInterceptor());
        return factory(builder.Options);
    }

    public static FixedClock Clock() => new(new DateTimeOffset(2026, 11, 19, 8, 0, 0, TimeSpan.Zero));

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }

    private string ConnectionString(string database) =>
        $"Host={_container!.Hostname};Port={_container.GetMappedPublicPort(5432)};Database={database};Username=postgres;Password={Password}";
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
