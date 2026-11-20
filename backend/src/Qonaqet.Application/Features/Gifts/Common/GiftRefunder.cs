using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Payments.Common;
using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Payments;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Gifts.Common;

/// <summary>
/// Hədiyyənin ödəyiciyə refundu (ləğvdə tam, müddət bitməsində qalıq): <see cref="PaymentRefunds"/> ilə provayderdə, sonra hədiyyə işarələnir.
/// İdempotentdir: refundun səbəbi hədiyyədən deterministik törəyir və eyni səbəbli uğurlu/gözləyən refund varsa ikincisi yaradılmır. Provayder
/// əlçatmazdırsa hədiyyədə <c>refundDueQepik</c> qalır və <c>gift-refunds</c> job-u təkrar cəhd edir. <c>Refund</c> ledger yazısını
/// <c>PaymentRefunded</c> hadisəsi ilə ledger consumer-i yazır.
/// </summary>
public sealed class GiftRefunder(IGiftRepository gifts, IPaymentRepository payments, PaymentRefunds refunds, IClock clock)
{
    private const int MaxMarkAttempts = 3;

    /// <summary>Gözləyən refundu icra edir; hədiyyə refund olunubsa (indi və ya əvvəl) <c>true</c>.</summary>
    public async Task<bool> RefundAsync(Gift gift, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gift);
        if (gift.RefundDueQepik <= 0 || gift.PaymentId is not { } rawPaymentId || gift.RefundReason is not { } reason)
        {
            return false;
        }

        var paymentId = PaymentId.From(rawPaymentId);
        var marker = Reason(gift.Id, reason);
        var existing = (await payments.ListRefundsAsync(paymentId, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(r => r.Reason == marker && r.Status != RefundStatus.Failed);
        if (existing is { Status: RefundStatus.Requested })
        {
            // Əvvəlki cəhd provayderə gedib, nəticəsi yazılmayıb: admin yoxlamalıdır, ikinci refund göndərilmir.
            return false;
        }

        long amount;
        if (existing is { Status: RefundStatus.Succeeded })
        {
            amount = existing.Amount;
        }
        else
        {
            var result = await refunds.RefundAsync(paymentId, gift.RefundDueQepik, marker, requestedBy: null, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return false;
            }

            amount = gift.RefundDueQepik;
        }

        return await MarkAsync(gift.Id, amount, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Provayderə gedən səbəb: şəxsi məlumatsız, hədiyyə və səbəb üzrə unikal.</summary>
    public static string Reason(GiftId giftId, GiftRefundReason reason) =>
        $"QONAQET hədiyyə {(reason == GiftRefundReason.Cancelled ? "ləğvi" : "müddəti bitdi")} · {giftId.Value:N}";

    private async Task<bool> MarkAsync(GiftId giftId, long amount, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxMarkAttempts; attempt++)
        {
            var gift = await gifts.GetAsync(giftId, cancellationToken).ConfigureAwait(false);
            if (gift is null)
            {
                return false;
            }

            var version = gift.Version;
            if (!gift.MarkRefunded(amount, clock.UtcNow))
            {
                return gift.RefundedQepik > 0;
            }

            if (await gifts.TryReplaceAsync(gift, version, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }
}
