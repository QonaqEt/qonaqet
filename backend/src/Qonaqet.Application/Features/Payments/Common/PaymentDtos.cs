using Qonaqet.Domain.Payments;

namespace Qonaqet.Application.Features.Payments.Common;

/// <summary>
/// Ödəyicinin gördüyü ödəniş (<c>GET payments/{id}</c>): tətbiq ödəniş səhifəsindən sonra bunu sorğulayır. <see cref="PaymentUrl"/> yalnız
/// <c>Pending</c>-də verilir.
/// </summary>
public sealed record PaymentDto(
    Guid Id,
    PaymentStatus Status,
    PaymentPurpose Purpose,
    Guid ReferenceId,
    long AmountQepik,
    long RefundedQepik,
    Uri? PaymentUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt);

public sealed record RefundDto(Guid Id, long AmountQepik, string Reason, RefundStatus Status, string? ProviderRef, Guid? RequestedBy, DateTimeOffset CreatedAt);

/// <summary>Admin görünüşü: provayder məlumatı və refundlar.</summary>
public sealed record AdminPaymentDto(
    PaymentDto Payment,
    string Provider,
    string ProviderOrderId,
    Guid? PayerUserId,
    Guid? CorporateId,
    Guid? PartnerClientId,
    IReadOnlyList<RefundDto> Refunds);

public static class PaymentMappings
{
    public static PaymentDto ToDto(this Payment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);
        return new PaymentDto(
            payment.Id.Value,
            payment.Status,
            payment.Purpose,
            payment.ReferenceId,
            payment.Amount,
            payment.RefundedAmount,
            payment.Status == PaymentStatus.Pending ? new Uri(payment.PaymentUrl) : null,
            payment.CreatedAt,
            payment.PaidAt);
    }

    public static RefundDto ToDto(this Refund refund)
    {
        ArgumentNullException.ThrowIfNull(refund);
        return new RefundDto(refund.Id.Value, refund.Amount, refund.Reason, refund.Status, refund.ProviderRef, refund.RequestedBy, refund.CreatedAt);
    }
}
