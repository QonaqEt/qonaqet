using Microsoft.EntityFrameworkCore;
using Qonaqet.Infrastructure.Persistence.Postgres;

namespace Qonaqet.Infrastructure.Messaging.Inbox;

/// <summary>Broker-dən gələn inteqrasiya mesajı.</summary>
public sealed record IntegrationMessage(Guid MessageId, string Type, string Payload);

/// <summary>
/// Postgres-ə yazan consumer. <see cref="HandleAsync"/> inbox qeydi ilə <b>eyni</b> Postgres tranzaksiyasında
/// işləyir, ona görə eyni mesajın ikinci çatdırılması heç nə dəyişmir.
/// </summary>
public interface IIntegrationEventConsumer
{
    /// <summary>Sabit ad: queue adı və <c>inbox_messages.consumer</c>.</summary>
    string Name { get; }

    /// <summary>Dinlənən hadisə tipləri (routing key-lər).</summary>
    IReadOnlyCollection<string> EventTypes { get; }

    Task HandleAsync(IntegrationMessage message, QonaqetDbContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Commit-dən sonra törəmə iş görən consumer (məs. Valkey keşini yeniləmək). Yalnız mesaj ilk dəfə emal olunanda çağırılır; xətası
/// tranzaksiyanı geri qaytarmır, itən keş yeniləməsi növbəti oxuda əsas store-dan bərpa olunmalıdır.
/// </summary>
public interface IAfterCommitConsumer
{
    Task AfterCommitAsync(IntegrationMessage message, QonaqetDbContext context, CancellationToken cancellationToken);
}

/// <summary>İdempotent inbox: (<c>message_id</c>, <c>consumer</c>) unikal; dublikat atlanır.</summary>
public sealed class InboxProcessor(QonaqetDbContext context)
{
    /// <summary>Mesajı emal edir; artıq emal olunubsa <c>false</c> qaytarır.</summary>
    public async Task<bool> ProcessAsync(IIntegrationEventConsumer consumer, IntegrationMessage message, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        ArgumentNullException.ThrowIfNull(message);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var inserted = await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO qonaqet.inbox_messages (id, message_id, consumer, processed_at)
            VALUES ({Guid.CreateVersion7()}, {message.MessageId}, {consumer.Name}, {now})
            ON CONFLICT (message_id, consumer) DO NOTHING
            """,
            cancellationToken).ConfigureAwait(false);
        if (inserted == 0)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        await consumer.HandleAsync(message, context, cancellationToken).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        if (consumer is IAfterCommitConsumer afterCommit)
        {
            await afterCommit.AfterCommitAsync(message, context, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }
}
