namespace Qonaqet.SharedKernel;

/// <summary>Aqreqatın qeyri-generik görünüşü: UnitOfWork hadisələri buradan oxuyur.</summary>
public interface IAggregateRoot
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    /// <summary>Toplanmış hadisələri qaytarır və siyahını boşaldır (outbox-a bir dəfə yazılsın deyə).</summary>
    IReadOnlyList<IDomainEvent> DequeueDomainEvents();
}

/// <summary>Aqreqat kökü: tranzaksiya sərhədi və domen hadisələrinin mənbəyi.</summary>
public abstract class AggregateRoot<TId> : Entity<TId>, IAggregateRoot
    where TId : struct, IStronglyTypedId<TId>
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id, DateTimeOffset createdAt, Guid? createdBy = null)
        : base(id, createdAt, createdBy)
    {
    }

    protected AggregateRoot()
    {
    }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public IReadOnlyList<IDomainEvent> DequeueDomainEvents()
    {
        var events = _domainEvents.ToArray();
        _domainEvents.Clear();
        return events;
    }

    protected void Raise(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }
}
