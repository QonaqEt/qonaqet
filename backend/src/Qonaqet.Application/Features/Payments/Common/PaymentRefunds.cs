using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Domain.Payments;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Payments.Common;

/// <summary>
/// Tam və ya qismən refund (admin; E09-da hədiyyə müddəti/ləğvi). Addımlar:
/// <list type="number">
/// <item><c>Requested</c> refund + ödənişin versiyası bir <c>SaveChanges</c>-də yazılır: eyni anda ikinci sorğu versiya fərqi ilə dayanır,
/// gözləyən refundlar qalıqdan çıxılır, ona görə cəm heç vaxt ödənişi aşmır;</item>
/// <item>provayder çağırılır (POST təkrarlanmır);</item>
/// <item>nəticə yazılır: uğurda ödəniş <c>Refunded</c>/<c>PartiallyRefunded</c> olur və <c>PaymentRefunded</c> outbox-a düşür (ledger).</item>
/// </list>
/// Tranzaksiyasız işləyir (provayder çağırışı tranzaksiya içində saxlanılmır).
/// </summary>
public sealed class PaymentRefunds(IPaymentRepository payments, IPaymentGatewayRegistry gateways, IClock clock)
{
    private const int MaxAttempts = 3;

    /// <param name="amountQepik"><c>null</c> = qalan bütün məbləğ.</param>
    public async Task<Result<RefundId>> RefundAsync(PaymentId paymentId, long? amountQepik, string reason, Guid? requestedBy, CancellationToken cancellationToken)
    {
        var requested = await RequestAsync(paymentId, amountQepik, reason, requestedBy, cancellationToken).ConfigureAwait(false);
        if (requested.IsFailure)
        {
            return requested.Error;
        }

        var (refund, payment) = requested.Value;
        GatewayRefund response;
        try
        {
            response = await gateways.Get(payment.Provider)
                .RefundAsync(payment.ProviderOrderId, refund.Amount, refund.Reason, cancellationToken).ConfigureAwait(false);
        }
        catch (PaymentGatewayException ex)
        {
            refund.Fail(ex.ProviderCode ?? "gateway_error", clock.UtcNow);
            await payments.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return PaymentErrors.RefundRejected;
        }

        await ApplyAsync(paymentId, refund.Id, response.ProviderReference, cancellationToken).ConfigureAwait(false);
        return refund.Id;
    }

    private async Task<Result<(Refund Refund, Payment Payment)>> RequestAsync(
        PaymentId paymentId, long? amountQepik, string reason, Guid? requestedBy, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var payment = await payments.GetAsync(paymentId, cancellationToken).ConfigureAwait(false);
            if (payment is null)
            {
                return PaymentErrors.NotFound;
            }

            if (payment.Status is not (PaymentStatus.Paid or PaymentStatus.PartiallyRefunded))
            {
                return PaymentErrors.NotConfirmed;
            }

            var pending = (await payments.ListRefundsAsync(paymentId, cancellationToken).ConfigureAwait(false))
                .Where(r => r.Status == RefundStatus.Requested)
                .Sum(r => r.Amount);
            var available = payment.RefundableAmount - pending;
            var amount = amountQepik ?? available;
            if (amount <= 0 || amount > available)
            {
                return PaymentErrors.RefundExceedsPayment;
            }

            var refund = Refund.Request(paymentId, amount, reason, requestedBy, clock.UtcNow);
            payments.AddRefund(refund);
            payment.NoteRefundRequested(clock.UtcNow);
            try
            {
                await payments.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return (refund, payment);
            }
            catch (ConcurrencyConflictException) when (attempt < MaxAttempts)
            {
                payments.Reset();
            }
        }
    }

    /// <summary>Uğurlu refundu yazır; ödəniş eyni anda dəyişibsə yenidən oxuyub təkrarlayır.</summary>
    private async Task ApplyAsync(PaymentId paymentId, RefundId refundId, string? providerRef, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var payment = await payments.GetAsync(paymentId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Ödəniş tapılmadı.");
            var refund = (await payments.ListRefundsAsync(paymentId, cancellationToken).ConfigureAwait(false)).Single(r => r.Id == refundId);
            refund.Succeed(providerRef, clock.UtcNow);
            payment.ApplyRefund(refund.Id, refund.Amount, clock.UtcNow);
            try
            {
                await payments.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (ConcurrencyConflictException) when (attempt < MaxAttempts)
            {
                payments.Reset();
            }
        }
    }
}
