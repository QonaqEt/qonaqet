using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Behaviors;

/// <summary>
/// Ən daxili behavior, yalnız əmrlər üçün: tranzaksiya açır, handler uğurlu olarsa izlənən aqreqatların
/// domen hadisələrini <b>eyni tranzaksiyada</b> outbox-a yazır və commit edir. Uğursuz nəticə və ya exception
/// rollback edir — heç bir hadisə yazılmır.
/// </summary>
public sealed class UnitOfWorkBehavior<TRequest, TResponse>(IUnitOfWork unitOfWork, IAggregateTracker tracker, IOutbox outbox)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (request is not ICommandBase)
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }

        await unitOfWork.BeginAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var response = await next(cancellationToken).ConfigureAwait(false);
            if (response is Result { IsFailure: true })
            {
                await unitOfWork.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return response;
            }

            var events = tracker.Tracked.SelectMany(a => a.DequeueDomainEvents()).ToArray();
            if (events.Length > 0)
            {
                await outbox.AddAsync(events, cancellationToken).ConfigureAwait(false);
            }

            await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
            return response;
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }
}
