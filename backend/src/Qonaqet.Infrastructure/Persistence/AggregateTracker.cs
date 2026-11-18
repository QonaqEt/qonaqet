using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Persistence;

/// <summary>Sorğu (scope) daxilində izlənən aqreqatlar; eyni obyekt iki dəfə qeyd olunmur.</summary>
public sealed class AggregateTracker : IAggregateTracker
{
    private readonly HashSet<IAggregateRoot> _tracked = new(ReferenceEqualityComparer.Instance);

    public IReadOnlyCollection<IAggregateRoot> Tracked => _tracked;

    public void Track(IAggregateRoot aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _tracked.Add(aggregate);
    }
}
