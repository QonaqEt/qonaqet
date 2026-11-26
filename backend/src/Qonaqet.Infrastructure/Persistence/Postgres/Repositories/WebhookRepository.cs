using Microsoft.EntityFrameworkCore;
using Npgsql;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Common;
using Qonaqet.Domain.Integration;

namespace Qonaqet.Infrastructure.Persistence.Postgres.Repositories;

/// <summary>Webhook abunələri və çatdırılma jurnalı (EF Core). Jurnal append-only-dir: vəziyyət yeni sətirlə dəyişir.</summary>
public sealed class WebhookRepository(QonaqetDbContext context) : IWebhookRepository
{
    public void AddSubscription(WebhookSubscription subscription) => context.Set<WebhookSubscription>().Add(subscription);

    public async Task<WebhookSubscription?> GetSubscriptionAsync(WebhookSubscriptionId id, CancellationToken cancellationToken) =>
        await context.Set<WebhookSubscription>().FirstOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<WebhookSubscription>> ListSubscriptionsAsync(Guid clientId, CancellationToken cancellationToken) =>
        await context.Set<WebhookSubscription>().AsNoTracking().Where(s => s.ClientId == clientId).OrderBy(s => s.CreatedAt)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<bool> TryAppendAsync(WebhookDelivery row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        context.Set<WebhookDelivery>().Add(row);
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            context.Entry(row).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<IReadOnlyList<WebhookDelivery>> DueAsync(DateTimeOffset now, TimeSpan sendingTimeout, int limit, CancellationToken cancellationToken)
    {
        var stuckBefore = now - sendingTimeout;
        return await context.Set<WebhookDelivery>().FromSqlInterpolated(
                $"""
                SELECT d.* FROM qonaqet.webhook_deliveries d
                JOIN (
                    SELECT delivery_id, max(seq) AS seq FROM qonaqet.webhook_deliveries
                    WHERE delivery_id IN (
                        SELECT delivery_id FROM qonaqet.webhook_deliveries
                        WHERE (status IN ('Pending', 'Retrying') AND next_attempt_at <= {now}) OR (status = 'Sending' AND created_at <= {stuckBefore}))
                    GROUP BY delivery_id) latest ON latest.delivery_id = d.delivery_id AND latest.seq = d.seq
                WHERE (d.status IN ('Pending', 'Retrying') AND d.next_attempt_at <= {now}) OR (d.status = 'Sending' AND d.created_at <= {stuckBefore})
                ORDER BY d.next_attempt_at
                LIMIT {limit}
                """)
            .AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<WebhookDelivery?> LatestAsync(Guid deliveryId, CancellationToken cancellationToken) =>
        await context.Set<WebhookDelivery>().AsNoTracking().Where(d => d.DeliveryId == deliveryId).OrderByDescending(d => d.Seq)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    public async Task<PagedResult<WebhookDelivery>> ListLatestForClientAsync(Guid clientId, PageRequest page, CancellationToken cancellationToken)
    {
        var latest = context.Set<WebhookDelivery>().FromSqlInterpolated(
            $"SELECT DISTINCT ON (delivery_id) * FROM qonaqet.webhook_deliveries WHERE client_id = {clientId} ORDER BY delivery_id, seq DESC").AsNoTracking();
        var total = await latest.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var items = await latest.OrderByDescending(d => d.CreatedAt).Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PagedResult<WebhookDelivery>(items, total);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
