using Microsoft.Extensions.Logging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Domain.Payments;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Payments.Common;

/// <summary>Bir yoxlamanın nəticəsi (job hesabatı və testlər üçün).</summary>
public enum PaymentCheckOutcome
{
    /// <summary>Dəyişiklik yoxdur (hələ gözləyir və ya artıq yekunlaşıb).</summary>
    Unchanged,

    Paid,

    Failed,

    Expired,

    /// <summary>Provayder cavab vermədi; ödəniş <c>Pending</c> qalır (pul tutulmuş ola bilər, ona görə müddətlə bağlanmır).</summary>
    GatewayError,
}

/// <summary>
/// Ödənişin statusunu <b>həmişə provayderin Get Order cavabı ilə</b> təsdiqləyir (Payriff callback-i imzasızdır, yalnız tetikleyicidir).
/// <c>Paid</c> keçidi <c>PaymentConfirmed</c> hadisəsi ilə eyni <c>SaveChanges</c>-də (eyni Postgres tranzaksiyası) yazılır. Eyni ödənişi eyni
/// anda callback və job yoxlasa, versiya nişanı ikincini dayandırır və o, yenidən oxuyub artıq yekunlaşmış ödənişi görür — hadisə bir dəfə yazılır.
/// </summary>
public sealed partial class PaymentConfirmation(
    IPaymentRepository payments, IPaymentGatewayRegistry gateways, IClock clock, ILogger<PaymentConfirmation> logger)
{
    /// <summary>Bu müddətdən köhnə <c>Pending</c> ödəniş (provayder hələ də ödənilmədi deyirsə) <c>Expired</c> olur.</summary>
    public static readonly TimeSpan ExpireAfter = TimeSpan.FromMinutes(30);

    private const int MaxAttempts = 3;

    /// <param name="expireIfStale">Job: 30 dəqiqədən köhnə ödənilməmiş sifarişi <c>Expired</c> edir. Callback bunu etmir.</param>
    public async Task<PaymentCheckOutcome> CheckAsync(PaymentId paymentId, bool expireIfStale, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var payment = await payments.GetAsync(paymentId, cancellationToken).ConfigureAwait(false);
            if (payment is null || payment.Status != PaymentStatus.Pending && payment.Status != PaymentStatus.Expired)
            {
                return PaymentCheckOutcome.Unchanged;
            }

            GatewayOrderStatus status;
            try
            {
                status = await gateways.Get(payment.Provider).GetOrderAsync(payment.ProviderOrderId, cancellationToken).ConfigureAwait(false);
            }
            catch (PaymentGatewayException ex)
            {
                LogGatewayError(ex, payment.Id.Value);
                return PaymentCheckOutcome.GatewayError;
            }

            var outcome = Apply(payment, status, expireIfStale);
            if (outcome == PaymentCheckOutcome.Unchanged)
            {
                return outcome;
            }

            try
            {
                await payments.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return outcome;
            }
            catch (ConcurrencyConflictException) when (attempt < MaxAttempts)
            {
                payments.Reset();
            }
        }
    }

    private PaymentCheckOutcome Apply(Payment payment, GatewayOrderStatus status, bool expireIfStale)
    {
        var now = clock.UtcNow;
        if (status.State == GatewayPaymentState.Paid)
        {
            if (status.AmountQepik != payment.Amount)
            {
                // Provayder başqa məbləğ deyirsə avtomatik təsdiq edilmir: admin baxmalıdır.
                LogAmountMismatch(payment.Id.Value, payment.Amount, status.AmountQepik);
                return PaymentCheckOutcome.Unchanged;
            }

            return payment.MarkPaid(now, status.ProviderStatus) ? PaymentCheckOutcome.Paid : PaymentCheckOutcome.Unchanged;
        }

        if (payment.Status != PaymentStatus.Pending)
        {
            return PaymentCheckOutcome.Unchanged;
        }

        if (status.State == GatewayPaymentState.Failed)
        {
            return payment.MarkFailed(now, status.ProviderStatus) ? PaymentCheckOutcome.Failed : PaymentCheckOutcome.Unchanged;
        }

        return expireIfStale && now - payment.CreatedAt >= ExpireAfter && payment.MarkExpired(now, status.ProviderStatus)
            ? PaymentCheckOutcome.Expired
            : PaymentCheckOutcome.Unchanged;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ödəniş {PaymentId} provayderdə yoxlanıla bilmədi; Pending qalır")]
    private partial void LogGatewayError(Exception exception, Guid paymentId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Ödəniş {PaymentId}: provayder məbləği ({Provider}) bizimkindən ({Expected}) fərqlidir; təsdiq edilmədi")]
    private partial void LogAmountMismatch(Guid paymentId, long expected, long provider);
}
