using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Behaviors;

/// <summary>
/// Ən daxili behavior, yalnız əmrlər üçün: tranzaksiya açır, handler uğurlu olarsa izlənən aqreqatların
/// domen hadisələrini <b>eyni tranzaksiyada</b> outbox-a yazır və commit edir. Uğursuz nəticə və ya exception
/// rollback edir — heç bir hadisə yazılmır (<see cref="ICommitsOnFailure"/> əmrləri istisna: uğursuzluq da commit olunur).
/// <see cref="INonTransactionalCommand"/> əmrləri tranzaksiyasız işləyir (atomik tək sənədli addımlar + kompensasiya).
/// </summary>
public sealed class UnitOfWorkBehavior<TRequest, TResponse>(IUnitOfWork unitOfWork, IPostgresUnitOfWork postgresUnitOfWork, IAggregateTracker tracker, IOutbox outbox)
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

        if (request is IPostgresCommand)
        {
            return await HandlePostgresAsync(request, next, cancellationToken).ConfigureAwait(false);
        }

        if (request is INonTransactionalCommand)
        {
            var result = await next(cancellationToken).ConfigureAwait(false);
            if (result is Result { IsSuccess: true })
            {
                var pending = tracker.Tracked.SelectMany(a => a.DequeueDomainEvents()).ToArray();
                if (pending.Length > 0)
                {
                    await outbox.AddAsync(pending, cancellationToken).ConfigureAwait(false);
                }
            }

            return result;
        }

        await unitOfWork.BeginAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var response = await next(cancellationToken).ConfigureAwait(false);
            if (response is Result { IsFailure: true } && request is not ICommitsOnFailure)
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

    /// <summary>Postgres əmri: hadisələri interceptor eyni <c>SaveChanges</c>-də outbox-a yazır, ona görə burada yalnız tranzaksiya idarə olunur.</summary>
    private async Task<TResponse> HandlePostgresAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is INonTransactionalCommand)
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }

        await postgresUnitOfWork.BeginAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var response = await next(cancellationToken).ConfigureAwait(false);
            if (response is Result { IsFailure: true } && request is not ICommitsOnFailure)
            {
                await postgresUnitOfWork.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return response;
            }

            await postgresUnitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
            return response;
        }
        catch
        {
            await postgresUnitOfWork.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }
}
