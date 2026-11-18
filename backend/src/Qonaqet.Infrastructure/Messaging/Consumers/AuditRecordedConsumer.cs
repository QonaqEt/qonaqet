using System.Text.Json;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Infrastructure.Messaging.Inbox;
using Qonaqet.Infrastructure.Messaging.Outbox;
using Qonaqet.Infrastructure.Persistence.Postgres;
using Qonaqet.Infrastructure.Persistence.Postgres.Records;

namespace Qonaqet.Infrastructure.Messaging.Consumers;

/// <summary>Mongo tərəfinin <see cref="AuditRecorded"/> hadisələrini Postgres <c>audit_logs</c>-a köçürür.</summary>
public sealed class AuditRecordedConsumer : IIntegrationEventConsumer
{
    public static readonly string EventType = typeof(AuditRecorded).FullName!;

    public string Name => "audit-log";

    public IReadOnlyCollection<string> EventTypes { get; } = [EventType];

    public Task HandleAsync(IntegrationMessage message, QonaqetDbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);

        var audit = JsonSerializer.Deserialize<AuditPayload>(message.Payload, OutboxSerializer.JsonOptions)
            ?? throw new InvalidOperationException("Boş AuditRecorded mesajı.");

        context.AuditLogs.Add(new AuditLogRecord
        {
            // Hadisə ID-si = audit qeydinin ID-si: təkrar emal olunsa belə eyni sətir olardı.
            Id = message.MessageId,
            ActorId = audit.ActorId,
            ActorType = audit.ActorType,
            Action = audit.Action,
            EntityType = audit.EntityType,
            EntityId = audit.EntityId,
            Data = JsonSerializer.Serialize(audit.Data ?? new Dictionary<string, string?>(), OutboxSerializer.JsonOptions),
            CreatedAt = audit.OccurredAt,
        });
        return Task.CompletedTask;
    }

    private sealed record AuditPayload(
        DateTimeOffset OccurredAt,
        Guid? ActorId,
        string ActorType,
        string Action,
        string EntityType,
        Guid EntityId,
        Dictionary<string, string?>? Data);
}
