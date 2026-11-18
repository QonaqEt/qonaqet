using Microsoft.Extensions.Logging;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Messaging.Outbox;

/// <summary>Relay-in gördüyü outbox mesajı (store-dan asılı olmayan forma).</summary>
public sealed record PendingOutboxMessage(Guid Id, string Type, string Payload, DateTimeOffset OccurredAt, int Attempts);

/// <summary>
/// Bir store-un outbox-u (Mongo <c>outboxMessages</c> və ya Postgres <c>outbox_messages</c>).
/// <see cref="ClaimAsync"/> mesajları <b>icarə</b> ilə götürür ki, iki relay instansı eyni mesajı eyni vaxtda
/// göndərməsin; icarə bitərsə (instans çöküb) mesaj yenidən götürülür. Çatdırılma ən azı bir dəfədir —
/// dublikatları consumer-lərin inbox-u atır.
/// </summary>
public interface IOutboxStore
{
    string Name { get; }

    Task<IReadOnlyList<PendingOutboxMessage>> ClaimAsync(int batchSize, DateTimeOffset now, TimeSpan lease, CancellationToken cancellationToken);

    Task MarkProcessedAsync(Guid id, DateTimeOffset processedAt, CancellationToken cancellationToken);

    Task MarkFailedAsync(Guid id, string error, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken);
}

/// <summary>Inteqrasiya hadisələrini broker-ə göndərir (RabbitMQ; testlərdə fake).</summary>
public interface IEventBus
{
    Task PublishAsync(Guid messageId, string type, string payload, DateTimeOffset occurredAt, CancellationToken cancellationToken);
}

public sealed class OutboxRelayOptions
{
    public const string SectionName = "Outbox";

    public int BatchSize { get; set; } = 100;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan Lease { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// Hər iki outbox-u broker-ə ötürür. Uğursuz göndəriş <c>attempts</c>-ı artırır və eksponensial backoff
/// (2^n saniyə, <see cref="OutboxRelayOptions.MaxBackoff"/>-a qədər) ilə yenidən planlaşdırılır. Heç bir
/// distributed tranzaksiya yoxdur: store-a yazı ilə broker-ə göndəriş ayrı addımlardır.
/// </summary>
public sealed partial class OutboxRelay(IEnumerable<IOutboxStore> stores, IEventBus bus, IClock clock, OutboxRelayOptions options, ILogger<OutboxRelay> logger)
{
    private readonly IReadOnlyList<IOutboxStore> _stores = stores.ToArray();

    /// <summary>Bir keçid: hər store-dan bir partiya. Göndərilən mesajların sayını qaytarır.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var published = 0;
        foreach (var store in _stores)
        {
            var batch = await store.ClaimAsync(options.BatchSize, clock.UtcNow, options.Lease, cancellationToken).ConfigureAwait(false);
            foreach (var message in batch)
            {
                try
                {
                    await bus.PublishAsync(message.Id, message.Type, message.Payload, message.OccurredAt, cancellationToken).ConfigureAwait(false);
                    await store.MarkProcessedAsync(message.Id, clock.UtcNow, cancellationToken).ConfigureAwait(false);
                    published++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    var next = clock.UtcNow + Backoff(message.Attempts + 1, options.MaxBackoff);
                    LogFailed(ex, store.Name, message.Id, message.Attempts + 1);
                    await store.MarkFailedAsync(message.Id, Shorten(ex.Message), next, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return published;
    }

    /// <summary>1-ci uğursuzluq → 2 s, 2-ci → 4 s, … , maksimum <paramref name="max"/>.</summary>
    public static TimeSpan Backoff(int attempts, TimeSpan max)
    {
        var seconds = Math.Pow(2, Math.Clamp(attempts, 1, 30));
        return TimeSpan.FromSeconds(Math.Min(seconds, max.TotalSeconds));
    }

    private static string Shorten(string value) => value.Length <= 2000 ? value : value[..2000];

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox mesajı göndərilmədi ({Store}, {MessageId}, cəhd {Attempt})")]
    private partial void LogFailed(Exception exception, string store, Guid messageId, int attempt);
}
