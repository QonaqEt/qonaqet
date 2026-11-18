using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Infrastructure.Persistence.Postgres.Interceptors;
using Qonaqet.Infrastructure.Persistence.Postgres.Records;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Persistence.Postgres;

/// <summary>Bir əmr = bir Postgres tranzaksiyası; commit-dən əvvəl <c>SaveChanges</c> (interceptor-lar işləyir).</summary>
public sealed class PostgresUnitOfWork(QonaqetDbContext context) : IUnitOfWork
{
    private IDbContextTransaction? _transaction;

    public async Task BeginAsync(CancellationToken cancellationToken)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException("Postgres tranzaksiyası artıq açıqdır.");
        }

        _transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        var transaction = _transaction ?? throw new InvalidOperationException("Açıq Postgres tranzaksiyası yoxdur.");
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            _transaction = null;
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        if (_transaction is null)
        {
            return;
        }

        try
        {
            await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await _transaction.DisposeAsync().ConfigureAwait(false);
            _transaction = null;
            context.ChangeTracker.Clear();
        }
    }
}

/// <summary>Hadisələri kontekstə əlavə edir; cari tranzaksiyanın <c>SaveChanges</c>-i ilə birlikdə yazılır.</summary>
public sealed class PostgresOutbox(QonaqetDbContext context) : IOutbox
{
    public Task AddAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        context.OutboxMessages.AddRange(events.Select(OutboxInterceptor.ToMessage));
        return Task.CompletedTask;
    }
}

/// <summary><c>idempotency_records</c> üzərində idempotency store (Valkey keşi E01-T10 ilə).</summary>
public sealed class PostgresIdempotencyStore(QonaqetDbContext context) : IIdempotencyStore
{
    public async Task<IdempotencyRecord?> FindAsync(string scope, string key, CancellationToken cancellationToken)
    {
        var row = await context.IdempotencyRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ClientId == scope && r.Key == key, cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : ToRecord(row);
    }

    public async Task<IdempotencyRecord> AddAsync(IdempotencyRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        // Əmrin öz tranzaksiyasından asılı olmasın deyə birbaşa INSERT ... ON CONFLICT DO NOTHING.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO qonaqet.idempotency_records (id, client_id, key, request_hash, response, created_at)
            VALUES ({Guid.CreateVersion7()}, {record.Scope}, {record.Key}, {record.RequestHash}, {record.ResponseJson}::jsonb, {record.CreatedAt})
            ON CONFLICT (client_id, key) DO NOTHING
            """,
            cancellationToken).ConfigureAwait(false);

        return await FindAsync(record.Scope, record.Key, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Idempotency qeydi yazılmadı.");
    }

    private static IdempotencyRecord ToRecord(IdempotencyRecordRow row) =>
        new(row.ClientId, row.Key, row.RequestHash, row.Response, row.CreatedAt);

}
