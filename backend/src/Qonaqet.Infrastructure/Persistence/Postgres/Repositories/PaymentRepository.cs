using Microsoft.EntityFrameworkCore;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Domain.Payments;

namespace Qonaqet.Infrastructure.Persistence.Postgres.Repositories;

/// <summary><c>payments</c> / <c>refunds</c> EF Core üzərində. Hadisələr <c>SaveChanges</c>-də outbox interceptor-u ilə yazılır.</summary>
public sealed class PaymentRepository(QonaqetDbContext context) : IPaymentRepository
{
    public void Add(Payment payment) => context.Set<Payment>().Add(payment);

    public void AddRefund(Refund refund) => context.Set<Refund>().Add(refund);

    public async Task<Payment?> GetAsync(PaymentId id, CancellationToken cancellationToken) =>
        await context.Set<Payment>().FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);

    public async Task<Payment?> FindByProviderOrderIdAsync(string providerOrderId, CancellationToken cancellationToken) =>
        await context.Set<Payment>().FirstOrDefaultAsync(p => p.ProviderOrderId == providerOrderId, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Payment>> ListPendingAsync(DateTimeOffset createdBefore, int limit, CancellationToken cancellationToken) =>
        await context.Set<Payment>()
            .Where(p => p.Status == PaymentStatus.Pending && p.CreatedAt < createdBefore)
            .OrderBy(p => p.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Refund>> ListRefundsAsync(PaymentId paymentId, CancellationToken cancellationToken) =>
        await context.Set<Refund>().Where(r => r.PaymentId == paymentId).OrderBy(r => r.CreatedAt).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("Ödəniş eyni anda dəyişdirildi.", ex);
        }
    }

    public void Reset() => context.ChangeTracker.Clear();
}
