using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qonaqet.Infrastructure.Messaging.Consumers;
using Qonaqet.Infrastructure.Messaging.Inbox;
using Qonaqet.Infrastructure.Messaging.Outbox;
using Qonaqet.Infrastructure.Messaging.RabbitMq;
using Qonaqet.SharedKernel;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Qonaqet.Infrastructure.Messaging;

/// <summary>Worker host-unun mesajlaşma hissəsi: outbox relay + RabbitMQ consumer-ləri.</summary>
public static class MessagingHostRegistration
{
    public static IServiceCollection AddMessagingHost(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<OutboxRelayOptions>(configuration.GetSection(OutboxRelayOptions.SectionName));
        services.AddSingleton<IEventBus, RabbitMqEventBus>();

        services.AddScoped<IOutboxStore, MongoOutboxStore>();
        services.AddScoped<IOutboxStore, PostgresOutboxStore>();
        services.AddScoped(sp => new OutboxRelay(
            sp.GetServices<IOutboxStore>(),
            sp.GetRequiredService<IEventBus>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IOptions<OutboxRelayOptions>>().Value,
            sp.GetRequiredService<ILogger<OutboxRelay>>()));
        services.AddHostedService<OutboxRelayService>();

        services.AddScoped<InboxProcessor>();
        services.AddSingleton<IIntegrationEventConsumer, AuditRecordedConsumer>();
        services.AddSingleton<IIntegrationEventConsumer, SocialEventsConsumer>();
        services.Configure<LedgerOptions>(configuration.GetSection(LedgerOptions.SectionName));
        services.AddSingleton<IIntegrationEventConsumer, LedgerConsumer>();
        services.AddSingleton<IIntegrationEventConsumer, PaymentFulfillmentConsumer>();
        services.AddSingleton<IIntegrationEventConsumer, GiftEventsConsumer>();
        services.AddSingleton<IIntegrationEventConsumer, CorporateReportsConsumer>();
        services.AddSingleton<IIntegrationEventConsumer, ScoreConsumer>();
        services.AddSingleton<IIntegrationEventConsumer, PartnerGiftConsumer>();
        services.Configure<WebhookOptions>(configuration.GetSection(WebhookOptions.SectionName));
        services.AddSingleton<IIntegrationEventConsumer, WebhookConsumer>();
        services.AddHostedService<RabbitMqConsumerHost>();
        return services;
    }
}

/// <summary>Outbox relay-i fasiləsiz işlədir; boş keçiddən sonra <see cref="OutboxRelayOptions.PollInterval"/> gözləyir.</summary>
public sealed partial class OutboxRelayService(IServiceScopeFactory scopes, IOptions<OutboxRelayOptions> options, ILogger<OutboxRelayService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollInterval);
        do
        {
            try
            {
                int published;
                do
                {
                    await using var scope = scopes.CreateAsyncScope();
                    published = await scope.ServiceProvider.GetRequiredService<OutboxRelay>().RunOnceAsync(stoppingToken).ConfigureAwait(false);
                }
                while (published > 0 && !stoppingToken.IsCancellationRequested);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogRelayError(ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox relay keçidi uğursuz oldu")]
    private partial void LogRelayError(Exception exception);
}

/// <summary>
/// Hər consumer üçün davamlı queue (<c>{prefix}.{name}</c>), lazımi routing key-lərə bağlı. Uğur → ack;
/// xəta → nack (requeue yox) → dead-letter queue (<c>{prefix}.{name}.dead</c>), əl ilə baxış üçün.
/// </summary>
public sealed partial class RabbitMqConsumerHost(
    RabbitMqConnection connection,
    IEnumerable<IIntegrationEventConsumer> consumers,
    IServiceScopeFactory scopes,
    IClock clock,
    ILogger<RabbitMqConsumerHost> logger) : BackgroundService
{
    private readonly List<IChannel> _channels = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var conn = await connection.GetAsync(stoppingToken).ConfigureAwait(false);
        foreach (var consumer in consumers)
        {
            var channel = await conn.CreateChannelAsync(cancellationToken: stoppingToken).ConfigureAwait(false);
            _channels.Add(channel);
            await connection.DeclareTopologyAsync(channel, stoppingToken).ConfigureAwait(false);
            var queue = RabbitMqConnection.QueueName(connection.Options, consumer);
            await channel.BasicQosAsync(0, 16, false, stoppingToken).ConfigureAwait(false);

            var handler = new AsyncEventingBasicConsumer(channel);
            handler.ReceivedAsync += (_, delivery) => OnReceivedAsync(channel, consumer, delivery, stoppingToken);
            await channel.BasicConsumeAsync(queue, autoAck: false, handler, stoppingToken).ConfigureAwait(false);
        }

        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
    }

    private async Task OnReceivedAsync(IChannel channel, IIntegrationEventConsumer consumer, BasicDeliverEventArgs delivery, CancellationToken cancellationToken)
    {
        try
        {
            if (!Guid.TryParse(delivery.BasicProperties.MessageId, out var messageId))
            {
                throw new InvalidOperationException("Mesajın MessageId-si yoxdur.");
            }

            var message = new IntegrationMessage(messageId, delivery.BasicProperties.Type ?? delivery.RoutingKey, Encoding.UTF8.GetString(delivery.Body.Span));
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<InboxProcessor>().ProcessAsync(consumer, message, clock.UtcNow, cancellationToken).ConfigureAwait(false);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogConsumerFailed(ex, consumer.Name, delivery.BasicProperties.MessageId);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, cancellationToken).ConfigureAwait(false);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        foreach (var channel in _channels)
        {
            await channel.DisposeAsync().ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Consumer {Consumer} mesajı emal edə bilmədi ({MessageId}); dead-letter-ə göndərildi")]
    private partial void LogConsumerFailed(Exception exception, string consumer, string? messageId);
}
