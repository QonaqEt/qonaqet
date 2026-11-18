using System.Text;
using Microsoft.Extensions.Options;
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
public sealed class RabbitMqConnection(IOptions<RabbitMqOptions> options) : IAsyncDisposable
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

    /// <summary>Exchange-ləri elan edir (idempotent).</summary>
    public async Task DeclareTopologyAsync(IChannel channel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);
        await channel.ExchangeDeclareAsync(Options.Exchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        await channel.ExchangeDeclareAsync(Options.DeadLetterExchange, ExchangeType.Direct, durable: true, cancellationToken: cancellationToken).ConfigureAwait(false);
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
