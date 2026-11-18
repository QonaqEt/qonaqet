using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using Qonaqet.Infrastructure.Persistence.Postgres;
using Qonaqet.Infrastructure.Persistence.Postgres.Records;

namespace Qonaqet.Infrastructure.Messaging.Outbox;

/// <summary>Mongo outbox-u: hər mesaj atomik <c>FindOneAndUpdate</c> ilə icarəyə götürülür.</summary>
public sealed class MongoOutboxStore(IMongoDatabase database) : IOutboxStore
{
    private readonly IMongoCollection<OutboxMessage> _collection = database.GetCollection<OutboxMessage>(MongoOutbox.CollectionName);

    public string Name => "mongo";

    public async Task<IReadOnlyList<PendingOutboxMessage>> ClaimAsync(int batchSize, DateTimeOffset now, TimeSpan lease, CancellationToken cancellationToken)
    {
        var f = Builders<OutboxMessage>.Filter;
        var due = f.And(
            f.Eq(m => m.ProcessedAt, null),
            f.Or(f.Eq(m => m.NextAttemptAt, null), f.Lte(m => m.NextAttemptAt, now)),
            f.Or(f.Eq(m => m.LockedUntil, null), f.Lte(m => m.LockedUntil, now)));
        var claim = Builders<OutboxMessage>.Update.Set(m => m.LockedUntil, now + lease);
        var options = new FindOneAndUpdateOptions<OutboxMessage>
        {
            Sort = Builders<OutboxMessage>.Sort.Ascending(m => m.OccurredAt),
            ReturnDocument = ReturnDocument.After,
        };

        var claimed = new List<PendingOutboxMessage>();
        while (claimed.Count < batchSize)
        {
            var message = await _collection.FindOneAndUpdateAsync(due, claim, options, cancellationToken).ConfigureAwait(false);
            if (message is null)
            {
                break;
            }

            claimed.Add(new PendingOutboxMessage(message.Id, message.Type, message.Payload, message.OccurredAt, message.Attempts));
        }

        return claimed;
    }

    public Task MarkProcessedAsync(Guid id, DateTimeOffset processedAt, CancellationToken cancellationToken) =>
        _collection.UpdateOneAsync(
            m => m.Id == id,
            Builders<OutboxMessage>.Update.Set(m => m.ProcessedAt, processedAt).Set(m => m.LockedUntil, null).Inc(m => m.Attempts, 1),
            cancellationToken: cancellationToken);

    public Task MarkFailedAsync(Guid id, string error, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken) =>
        _collection.UpdateOneAsync(
            m => m.Id == id,
            Builders<OutboxMessage>.Update
                .Inc(m => m.Attempts, 1)
                .Set(m => m.LastError, error)
                .Set(m => m.NextAttemptAt, nextAttemptAt)
                .Set(m => m.LockedUntil, null),
            cancellationToken: cancellationToken);
}

/// <summary>Postgres outbox-u: <c>FOR UPDATE SKIP LOCKED</c> ilə partiya icarəyə götürülür.</summary>
public sealed class PostgresOutboxStore(QonaqetDbContext context) : IOutboxStore
{
    public string Name => "postgres";

    public async Task<IReadOnlyList<PendingOutboxMessage>> ClaimAsync(int batchSize, DateTimeOffset now, TimeSpan lease, CancellationToken cancellationToken)
    {
        var lockedUntil = now + lease;
        var rows = await context.OutboxMessages
            .FromSqlInterpolated(
                $"""
                UPDATE qonaqet.outbox_messages o SET locked_until = {lockedUntil}
                WHERE o.id IN (
                    SELECT id FROM qonaqet.outbox_messages
                    WHERE processed_at IS NULL
                      AND (next_attempt_at IS NULL OR next_attempt_at <= {now})
                      AND (locked_until IS NULL OR locked_until <= {now})
                    ORDER BY occurred_at
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED)
                RETURNING o.*
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.OrderBy(r => r.OccurredAt).Select(r => new PendingOutboxMessage(r.Id, r.Type, r.Payload, r.OccurredAt, r.Attempts)).ToArray();
    }

    public Task MarkProcessedAsync(Guid id, DateTimeOffset processedAt, CancellationToken cancellationToken) =>
        context.OutboxMessages.Where(m => m.Id == id).ExecuteUpdateAsync(
            s => s.SetProperty(m => m.ProcessedAt, processedAt).SetProperty(m => m.LockedUntil, (DateTimeOffset?)null).SetProperty(m => m.Attempts, m => m.Attempts + 1),
            cancellationToken);

    public Task MarkFailedAsync(Guid id, string error, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken) =>
        context.OutboxMessages.Where(m => m.Id == id).ExecuteUpdateAsync(
            s => s.SetProperty(m => m.Attempts, m => m.Attempts + 1)
                .SetProperty(m => m.LastError, error)
                .SetProperty(m => m.NextAttemptAt, nextAttemptAt)
                .SetProperty(m => m.LockedUntil, (DateTimeOffset?)null),
            cancellationToken);
}
