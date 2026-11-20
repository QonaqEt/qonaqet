using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Persistence.Mongo;
using Qonaqet.IntegrationTests.Persistence.Postgres;

namespace Qonaqet.IntegrationTests.Payments;

/// <summary>Mongo + Postgres (ödəniş, ledger, payout testləri). Hər host öz Mongo və Postgres bazasını alır.</summary>
public sealed class PaymentsFixture : IAsyncLifetime
{
    public MongoFixture Mongo { get; } = new();

    public PostgresFixture Postgres { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await Mongo.InitializeAsync().ConfigureAwait(false);
        await Postgres.InitializeAsync().ConfigureAwait(false);
    }

    public void SkipIfUnavailable()
    {
        Mongo.SkipIfUnavailable();
        Postgres.SkipIfUnavailable();
    }

    /// <summary>Təmiz Mongo + Postgres bazası ilə real Api host-u.</summary>
    public async Task<ApiTestHost> HostAsync(string environment = "Development", Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? configure = null) =>
        new(Mongo, environment, configure, await Postgres.NewDatabaseAsync().ConfigureAwait(false));

    public async ValueTask DisposeAsync()
    {
        await Mongo.DisposeAsync().ConfigureAwait(false);
        await Postgres.DisposeAsync().ConfigureAwait(false);
    }
}

[CollectionDefinition(Name)]
public sealed class PaymentsCollection : ICollectionFixture<PaymentsFixture>
{
    public const string Name = "payments";
}
