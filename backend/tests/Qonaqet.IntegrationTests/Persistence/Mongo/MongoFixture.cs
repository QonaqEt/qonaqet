using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Infrastructure.Persistence.Mongo;

namespace Qonaqet.IntegrationTests.Persistence.Mongo;

/// <summary>
/// Tək node-lu replica set (tranzaksiyalar üçün) Testcontainers-in generic konteyneri ilə; image
/// <c>deploy/docker-compose.yml</c>-dəki ilə eyni pinlənib. Docker yoxdursa lokal testlər skip olunur,
/// CI-da (<c>CI=true</c>) isə xəta verir ki, testlər səssizcə buraxılmasın.
/// </summary>
public sealed class MongoFixture : IAsyncLifetime
{
    public const string Image = "mongo:8.0.38-noble";

    private IContainer? _container;

    public string? UnavailableReason { get; private set; }

    public IMongoClient Client { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        MongoConventions.Register();
        try
        {
            _container = new ContainerBuilder(Image)
                .WithCommand("--replSet", "rs0", "--bind_ip_all")
                .WithPortBinding(27017, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("mongosh", "--quiet", "--eval", "db.adminCommand('ping')"))
                .Build();
            await _container.StartAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (Environment.GetEnvironmentVariable("CI") != "true")
        {
            UnavailableReason = $"Docker əlçatan deyil: {ex.Message}";
            return;
        }

        await _container.ExecAsync(["mongosh", "--quiet", "--eval", "rs.initiate({_id:'rs0',members:[{_id:0,host:'localhost:27017'}]})"]).ConfigureAwait(false);

        var connection = $"mongodb://{_container.Hostname}:{_container.GetMappedPublicPort(27017)}/?directConnection=true";
        Client = new MongoClient(connection);
        await WaitForPrimaryAsync().ConfigureAwait(false);
    }

    /// <summary>Hər test üçün təmiz verilənlər bazası.</summary>
    public IMongoDatabase NewDatabase() => Client.GetDatabase("t_" + Guid.NewGuid().ToString("N")[..12]);

    public void SkipIfUnavailable() => Assert.SkipWhen(UnavailableReason is not null, UnavailableReason ?? string.Empty);

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task WaitForPrimaryAsync()
    {
        var admin = Client.GetDatabase("admin");
        for (var i = 0; i < 60; i++)
        {
            var hello = await admin.RunCommandAsync<BsonDocument>(new BsonDocument("hello", 1)).ConfigureAwait(false);
            if (hello.GetValue("isWritablePrimary", false).ToBoolean())
            {
                return;
            }

            await Task.Delay(500).ConfigureAwait(false);
        }

        throw new InvalidOperationException("Mongo replica set primary olmadı.");
    }
}

[CollectionDefinition(Name)]
public sealed class MongoCollection : ICollectionFixture<MongoFixture>
{
    public const string Name = "mongo";
}
