using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Domain.Corporate;

namespace Qonaqet.Application.Features.Corporate.Events;

/// <summary>
/// Korporativ sifarişin kart ödənişinin nəticəsi (<c>PaymentFulfillment</c>-dən, sessiyasız və idempotent): təsdiqdə <c>AwaitingPayment</c> →
/// <c>Paid</c> (fan-out job-u hədiyyələri yaradır), uğursuzluqda sifariş qaralamaya qayıdır. Keçidlər status şərti ilə yazılır.
/// </summary>
public sealed class CorporateOrderPayments(ICorporateGiftOrderRepository orders, IOutbox outbox)
{
    public async Task<bool> OnPaidAsync(CorporateGiftOrderId orderId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (order is null || !order.MarkPaid(null, at, null))
        {
            return false;
        }

        if (!await orders.TryReplaceAsync(order, CorporateOrderStatus.AwaitingPayment, order.SentCount, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await outbox.AddAsync(order.DequeueDomainEvents(), cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Yalnız sifarişin cari ödənişi bağlananda (köhnə, artıq əvəzlənmiş cəhd sifarişə toxunmur).</summary>
    public async Task<bool> OnUnpaidAsync(CorporateGiftOrderId orderId, Guid paymentId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(orderId, cancellationToken).ConfigureAwait(false);
        return order is not null && order.PaymentId == paymentId && order.PaymentFailed(at)
            && await orders.TryReplaceAsync(order, CorporateOrderStatus.AwaitingPayment, order.SentCount, cancellationToken).ConfigureAwait(false);
    }
}
