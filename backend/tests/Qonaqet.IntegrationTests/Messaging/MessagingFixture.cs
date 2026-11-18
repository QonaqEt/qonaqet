using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Qonaqet.IntegrationTests.Persistence.Mongo;
using Qonaqet.IntegrationTests.Persistence.Postgres;

namespace Qonaqet.IntegrationTests.Messaging;

/// <summary>Uçdan-uca mesajlaşma testləri üçün Mongo + Postgres + RabbitMQ (compose-dakı image-lər).</summary>
public sealed class MessagingFixture : IAsyncLifetime
{
    public const string RabbitImage = "rabbitmq:4.4.0-management-alpine";

    private IContainer? _rabbit;

    public MongoFixture Mongo { get; } = new();

    public PostgresFixture Postgres { get; } = new();

    public string? UnavailableReason { get; private set; }

    public Uri RabbitUri { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await Mongo.InitializeAsync().ConfigureAwait(false);
        await Postgres.InitializeAsync().ConfigureAwait(false);
        UnavailableReason = Mongo.UnavailableReason ?? Postgres.UnavailableReason;
        if (UnavailableReason is not null)
        {
            return;
        }

        try
        {
            _rabbit = new ContainerBuilder(RabbitImage)
                .WithEnvironment("RABBITMQ_DEFAULT_USER", "test")
                .WithEnvironment("RABBITMQ_DEFAULT_PASS", "test-only")
                .WithPortBinding(5672, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server startup complete"))
                .Build();
            await _rabbit.StartAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (Environment.GetEnvironmentVariable("CI") != "true")
        {
            UnavailableReason = $"Docker əlçatan deyil: {ex.Message}";
            return;
        }

        RabbitUri = new Uri($"amqp://test:test-only@{_rabbit.Hostname}:{_rabbit.GetMappedPublicPort(5672)}/");
    }

    public void SkipIfUnavailable() => Assert.SkipWhen(UnavailableReason is not null, UnavailableReason ?? string.Empty);

    public async ValueTask DisposeAsync()
    {
        if (_rabbit is not null)
        {
            await _rabbit.DisposeAsync().ConfigureAwait(false);
        }

        await Mongo.DisposeAsync().ConfigureAwait(false);
        await Postgres.DisposeAsync().ConfigureAwait(false);
    }
}

[CollectionDefinition(Name)]
public sealed class MessagingCollection : ICollectionFixture<MessagingFixture>
{
    public const string Name = "messaging";
}
