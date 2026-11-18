using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Infrastructure;
using Qonaqet.Infrastructure.Messaging;
using Qonaqet.Infrastructure.Messaging.Outbox;
using Qonaqet.Infrastructure.Messaging.RabbitMq;
using Qonaqet.Infrastructure.Persistence.Mongo;
using Qonaqet.Infrastructure.Persistence.Postgres;
using Qonaqet.Infrastructure.Persistence.Postgres.Records;
using Qonaqet.IntegrationTests.Persistence.Mongo;
using Qonaqet.SharedKernel;
using RabbitMQ.Client;

namespace Qonaqet.IntegrationTests.Messaging;

/// <summary>Mongo dəyişikliyi → outbox → relay → RabbitMQ → inbox → Postgres yazısı (E01-T09).</summary>
[Collection(MessagingCollection.Name)]
public sealed class OutboxEndToEndTests(MessagingFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Mongo_soft_delete_reaches_postgres_audit_logs_exactly_once()
    {
        fixture.SkipIfUnavailable();
        await using var provider = await BuildAsync();
        var consumerHost = provider.GetServices<IHostedService>().OfType<RabbitMqConsumerHost>().Single();
        await consumerHost.StartAsync(Ct);

        var widgetId = await SoftDeleteWidgetAsync(provider);

        await using (var scope = provider.CreateAsyncScope())
        {
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<OutboxRelay>().RunOnceAsync(Ct));
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<OutboxRelay>().RunOnceAsync(Ct));
        }

        var audit = await WaitForAuditAsync(provider, widgetId, expected: 1);
        Assert.Equal(AuditActions.SoftDelete, audit.Single().Action);

        // Eyni mesajın təkrar çatdırılması (at-least-once) heç nə dəyişmir.
        var message = await provider.GetRequiredService<IMongoDatabase>().GetCollection<OutboxMessage>(MongoOutbox.CollectionName)
            .Find(FilterDefinition<OutboxMessage>.Empty).SingleAsync(Ct);
        Assert.NotNull(message.ProcessedAt);
        await provider.GetRequiredService<IEventBus>().PublishAsync(message.Id, message.Type, message.Payload, message.OccurredAt, Ct);
        await WaitForInboxAsync(provider, message.Id);
        await Task.Delay(500, Ct);
        Assert.Single(await WaitForAuditAsync(provider, widgetId, expected: 1));

        await consumerHost.StopAsync(Ct);
    }

    [Fact]
    public async Task Failing_consumer_message_goes_to_the_dead_letter_queue()
    {
        fixture.SkipIfUnavailable();
        await using var provider = await BuildAsync();
        var consumerHost = provider.GetServices<IHostedService>().OfType<RabbitMqConsumerHost>().Single();
        await consumerHost.StartAsync(Ct);

        var bus = provider.GetRequiredService<IEventBus>();
        await bus.PublishAsync(Guid.CreateVersion7(), typeof(AuditRecorded).FullName!, "not json", DateTimeOffset.UtcNow, Ct);

        var connection = provider.GetRequiredService<RabbitMqConnection>();
        await using var channel = await (await connection.GetAsync(Ct)).CreateChannelAsync(cancellationToken: Ct);
        var dead = $"{connection.Options.QueuePrefix}.audit-log.dead";
        BasicGetResult? result = null;
        for (var i = 0; i < 50 && result is null; i++)
        {
            result = await channel.BasicGetAsync(dead, autoAck: true, Ct);
            if (result is null)
            {
                await Task.Delay(200, Ct);
            }
        }

        Assert.NotNull(result);
        Assert.Equal("not json", Encoding.UTF8.GetString(result.Body.Span));
        await consumerHost.StopAsync(Ct);
    }

    [Fact]
    public async Task Postgres_outbox_claims_with_a_lease_and_respects_backoff()
    {
        fixture.SkipIfUnavailable();
        await using var provider = await BuildAsync();
        var now = DateTimeOffset.UtcNow;
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
        var id = Guid.CreateVersion7();
        context.OutboxMessages.Add(new PostgresOutboxMessage { Id = id, Type = "T", Payload = "{}", OccurredAt = now });
        await context.SaveChangesAsync(Ct);
        var store = new PostgresOutboxStore(context);

        Assert.Single(await store.ClaimAsync(10, now, TimeSpan.FromSeconds(30), Ct));
        Assert.Empty(await store.ClaimAsync(10, now, TimeSpan.FromSeconds(30), Ct)); // icarədədir

        await store.MarkFailedAsync(id, "x", now + TimeSpan.FromMinutes(1), Ct);
        Assert.Empty(await store.ClaimAsync(10, now, TimeSpan.FromSeconds(30), Ct)); // backoff
        var retried = Assert.Single(await store.ClaimAsync(10, now + TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(30), Ct));
        Assert.Equal(1, retried.Attempts);

        await store.MarkProcessedAsync(id, now, Ct);
        Assert.Empty(await store.ClaimAsync(10, now + TimeSpan.FromHours(1), TimeSpan.FromSeconds(30), Ct));
    }

    private async Task<ServiceProvider> BuildAsync()
    {
        var mongoDb = "e2e_" + Guid.NewGuid().ToString("N")[..10];
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mongo:ConnectionString"] = fixture.Mongo.ConnectionString,
            ["Mongo:Database"] = mongoDb,
            ["ConnectionStrings:Postgres"] = await fixture.Postgres.NewDatabaseAsync(),
            ["RabbitMq:Uri"] = fixture.RabbitUri.ToString(),
            ["RabbitMq:QueuePrefix"] = mongoDb,
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddInfrastructure(config);
        services.AddMessagingHost(config);
        services.AddScoped<TestWidgetRepository>();
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<QonaqetDbContext>().Database.MigrateAsync(Ct);
        var database = provider.GetRequiredService<IMongoDatabase>();
        await database.CreateCollectionAsync(TestWidgetRepository.CollectionNameValue, cancellationToken: Ct);
        await database.CreateCollectionAsync(MongoOutbox.CollectionName, cancellationToken: Ct);
        return provider;
    }

    private static async Task<TestWidgetId> SoftDeleteWidgetAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<TestWidgetRepository>();
        var widget = new TestWidget("e2e", scope.ServiceProvider.GetRequiredService<IClock>().UtcNow);
        await repository.AddAsync(widget, Ct);
        Assert.True(await repository.SoftDeleteAsync(widget.Id, Guid.CreateVersion7(), "e2e", Ct));
        return widget.Id;
    }

    private static async Task<List<AuditLogRecord>> WaitForAuditAsync(IServiceProvider provider, TestWidgetId id, int expected)
    {
        for (var i = 0; ; i++)
        {
            await using var scope = provider.CreateAsyncScope();
            var rows = await scope.ServiceProvider.GetRequiredService<QonaqetDbContext>().AuditLogs.Where(a => a.EntityId == id.Value).ToListAsync(Ct);
            if (rows.Count >= expected || i == 100)
            {
                return rows;
            }

            await Task.Delay(100, Ct);
        }
    }

    private static async Task WaitForInboxAsync(IServiceProvider provider, Guid messageId)
    {
        for (var i = 0; i < 100; i++)
        {
            await using var scope = provider.CreateAsyncScope();
            if (await scope.ServiceProvider.GetRequiredService<QonaqetDbContext>().InboxMessages.AnyAsync(m => m.MessageId == messageId, Ct))
            {
                return;
            }

            await Task.Delay(100, Ct);
        }
    }
}
