using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Common.Errors;

/// <summary>Ödənişlərin sabit xəta kodları.</summary>
public static class PaymentErrors
{
    public static readonly Error NotFound = Error.NotFound("payment_not_found", "Ödəniş tapılmadı.");

    public static readonly Error FakeCheckoutDisabled = Error.NotFound("not_found", "Səhifə tapılmadı.");

    public static readonly Error AlreadyCompleted = Error.Conflict("payment_already_completed", "Bu ödəniş artıq tamamlanıb.");

    public static readonly Error GatewayUnavailable =
        Error.Failure("payment_gateway_unavailable", "Ödəniş sistemi hazırda cavab vermir. Bir az sonra yenidən cəhd edin.");

    public static readonly Error NotConfirmed = Error.Conflict("payment_not_confirmed", "Ödəniş hələ təsdiqlənməyib.");

    public static readonly Error RefundExceedsPayment = Error.Validation("refund_exceeds_payment", "Refund məbləği ödənişin qalan hissəsini aşır.");

    public static readonly Error RefundRejected = Error.Conflict("refund_rejected", "Ödəniş sistemi refundu rədd etdi.");
}
