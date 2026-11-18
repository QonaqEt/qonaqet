using System.Text;
using Microsoft.Extensions.Options;
using Qonaqet.Infrastructure.Messaging.Inbox;
using RabbitMQ.Client;

namespace Qonaqet.Infrastructure.Messaging.RabbitMq;

/// <summary><c>RabbitMq</c> konfiqurasiya bölməsi.</summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public Uri Uri { get; set; } = new("amqp://guest:guest@localhost:5672/");

    /// <summary>Bütün inteqrasiya hadisələrinin topic exchange-i; routing key = hadisə tipi.</summary>
    public string Exchange { get; set; } = "qonaqet.events";

    public string DeadLetterExchange { get; set; } = "qonaqet.dead-letter";

    public string QueuePrefix { get; set; } = "qonaqet";
}

/// <summary>Prosesdə tək, tənbəl açılan RabbitMQ bağlantısı (avtomatik bərpa ilə).</summary>
public sealed class RabbitMqConnection(IOptions<RabbitMqOptions> options, IEnumerable<IIntegrationEventConsumer> consumers) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public RabbitMqOptions Options => options.Value;

    public async Task<IConnection> GetAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true } open)
        {
            return open;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is { IsOpen: true } existing)
            {
                return existing;
            }

            var factory = new ConnectionFactory
            {
                Uri = options.Value.Uri,
                AutomaticRecoveryEnabled = true,
                ClientProvidedName = "qonaqet",
            };
            _connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public static string QueueName(RabbitMqOptions options, IIntegrationEventConsumer consumer)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(consumer);
        return $"{options.QueuePrefix}.{consumer.Name}";
    }

    /// <summary>
    /// Exchange-ləri, hər consumer-in queue-sunu, dead-letter queue-sunu və binding-ləri elan edir (idempotent).
    /// Publisher də bunu çağırır ki, consumer hələ qalxmamış göndərilən hadisə itməsin.
    /// </summary>
    public async Task DeclareTopologyAsync(IChannel channel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var o = Options;
        await channel.ExchangeDeclareAsync(o.Exchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        await channel.ExchangeDeclareAsync(o.DeadLetterExchange, ExchangeType.Direct, durable: true, cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (var consumer in consumers)
        {
            var queue = QueueName(o, consumer);
            var dead = queue + ".dead";
            await channel.QueueDeclareAsync(dead, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken).ConfigureAwait(false);
            await channel.QueueBindAsync(dead, o.DeadLetterExchange, queue, cancellationToken: cancellationToken).ConfigureAwait(false);
            await channel.QueueDeclareAsync(
                queue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-dead-letter-exchange"] = o.DeadLetterExchange,
                    ["x-dead-letter-routing-key"] = queue,
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            foreach (var type in consumer.EventTypes)
            {
                await channel.QueueBindAsync(queue, o.Exchange, type, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }

        _gate.Dispose();
    }
}

/// <summary>Publisher confirm-ləri ilə davamlı (persistent) göndəriş: broker təsdiqləməsə exception atılır.</summary>
public sealed class RabbitMqEventBus(RabbitMqConnection connection) : Outbox.IEventBus, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IChannel? _channel;

    public async Task PublishAsync(Guid messageId, string type, string payload, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var channel = await ChannelAsync(cancellationToken).ConfigureAwait(false);
            var properties = new BasicProperties
            {
                MessageId = messageId.ToString(),
                Type = type,
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                Timestamp = new AmqpTimestamp(occurredAt.ToUnixTimeSeconds()),
            };
            await channel.BasicPublishAsync(
                connection.Options.Exchange, type, mandatory: false, properties, Encoding.UTF8.GetBytes(payload), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Kanal pozula bilər; növbəti göndəriş yenisini açsın.
            if (_channel is not null)
            {
                await _channel.DisposeAsync().ConfigureAwait(false);
                _channel = null;
            }

            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IChannel> ChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true } open)
        {
            return open;
        }

        var conn = await connection.GetAsync(cancellationToken).ConfigureAwait(false);
        _channel = await conn.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken).ConfigureAwait(false);
        await connection.DeclareTopologyAsync(_channel, cancellationToken).ConfigureAwait(false);
        return _channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync().ConfigureAwait(false);
        }

        _gate.Dispose();
    }
}
