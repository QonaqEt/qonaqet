using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Common;

/// <summary>
/// <see cref="Abstractions.Messaging.INonTransactionalCommand"/> əmrinin içində bir hissəni tranzaksiyada icra edir (UnitOfWork behavior-un etdiyi
/// kimi): uğurda izlənən aqreqatların hadisələri eyni tranzaksiyada outbox-a yazılır və commit olunur; uğursuzluq və exception rollback edir.
/// Bir əmrin içində müxtəlif budaqlar (claim tranzaksiyalı, hədiyyə təkrar cəhdli) olanda işlədilir.
/// </summary>
public sealed class MongoTransaction(IUnitOfWork unitOfWork, IAggregateTracker tracker, IOutbox outbox)
{
    public async Task<Result<T>> RunAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        await unitOfWork.BeginAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await work(cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                await unitOfWork.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }

            var events = tracker.Tracked.SelectMany(a => a.DequeueDomainEvents()).ToArray();
            if (events.Length > 0)
            {
                await outbox.AddAsync(events, cancellationToken).ConfigureAwait(false);
            }

            await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }
}
