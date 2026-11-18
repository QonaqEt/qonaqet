using Qonaqet.Application.Abstractions.Persistence;

namespace Qonaqet.Infrastructure.Persistence.Mongo;

/// <summary>Bir əmr = bir Mongo tranzaksiyası (replica set tələb olunur).</summary>
public sealed class MongoUnitOfWork(MongoContext context) : IUnitOfWork
{
    public async Task BeginAsync(CancellationToken cancellationToken)
    {
        var session = await context.Client.StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        session.StartTransaction();
        context.Attach(session);
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        var session = context.Session ?? throw new InvalidOperationException("Açıq Mongo tranzaksiyası yoxdur.");
        try
        {
            await session.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            context.Detach();
            session.Dispose();
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        var session = context.Session;
        if (session is null)
        {
            return;
        }

        try
        {
            if (session.IsInTransaction)
            {
                await session.AbortTransactionAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            context.Detach();
            session.Dispose();
        }
    }
}
