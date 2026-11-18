namespace Qonaqet.SharedKernel;

/// <summary>Aqreqat daxilində qaldırılan domen hadisəsi. UnitOfWork onu eyni tranzaksiyada outbox-a yazır.</summary>
public interface IDomainEvent
{
    /// <summary>Unikal hadisə ID-si (UUID v7); outbox <c>messageId</c> və consumer idempotentliyi üçün.</summary>
    Guid EventId { get; }

    DateTimeOffset OccurredAt { get; }
}

/// <summary>Domen hadisələri üçün baza record.</summary>
public abstract record DomainEvent : IDomainEvent
{
    protected DomainEvent(DateTimeOffset occurredAt)
    {
        OccurredAt = Guard.Utc(occurredAt);
    }

    public Guid EventId { get; init; } = Guid.CreateVersion7();

    public DateTimeOffset OccurredAt { get; init; }
}
