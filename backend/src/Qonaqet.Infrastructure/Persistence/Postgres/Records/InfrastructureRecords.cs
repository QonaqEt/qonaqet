using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Persistence.Postgres.Records;

/// <summary>Audit interceptor-u bu tipləri izləmir (öz-özünü audit etməsin deyə).</summary>
public interface INotAudited;

/// <summary><c>audit_logs</c> (append-only). <see cref="Data"/> JSON-dur; şəxsi məlumat yox, yalnız sahə adları/metadatalar.</summary>
public sealed class AuditLogRecord : IAppendOnly, INotAudited
{
    public Guid Id { get; set; }

    public Guid? ActorId { get; set; }

    public string ActorType { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public Guid? EntityId { get; set; }

    public string Data { get; set; } = "{}";

    public string? Ip { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary><c>outbox_messages</c> (append-only; relay yalnız <see cref="ProcessedAt"/>/<see cref="Attempts"/> yeniləyir).</summary>
public sealed class PostgresOutboxMessage : IAppendOnly, INotAudited
{
    /// <summary>= hadisənin <c>EventId</c>-si.</summary>
    public Guid Id { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Payload { get; set; } = "{}";

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }

    public string? LastError { get; set; }
}

/// <summary><c>inbox_messages</c> (append-only): consumer-in emal etdiyi mesaj; (<c>message_id</c>, <c>consumer</c>) unikaldır.</summary>
public sealed class InboxMessage : IAppendOnly, INotAudited
{
    public Guid Id { get; set; }

    public Guid MessageId { get; set; }

    public string Consumer { get; set; } = string.Empty;

    public DateTimeOffset ProcessedAt { get; set; }
}

/// <summary><c>idempotency_records</c> (tarixçə kimi saxlanır; Valkey surəti yalnız keşdir).</summary>
public sealed class IdempotencyRecordRow : IAppendOnly, INotAudited
{
    public Guid Id { get; set; }

    /// <summary>Aktor/klient + əmr tipi (behavior-un <c>scope</c>-u).</summary>
    public string ClientId { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public string RequestHash { get; set; } = string.Empty;

    public string Response { get; set; } = "null";

    public DateTimeOffset CreatedAt { get; set; }
}
