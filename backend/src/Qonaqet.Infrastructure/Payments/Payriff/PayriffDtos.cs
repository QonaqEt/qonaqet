using System.Text.Json.Serialization;

namespace Qonaqet.Infrastructure.Payments.Payriff;

// Payriff Gateway API v3 DTO-ları. Sahə adları rəsmi sənədlə yoxlanılıb (docs.payriff.com: Authorization, Create Order,
// Order Information, Refund/Reverse; 2026-11-20). Bütün cavablar eyni zərfdədir: code ("00000" = uğur), message, payload.

/// <summary><c>POST /api/v3/orders</c> body-si.</summary>
public sealed record PayriffCreateOrderRequest(
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("callbackUrl")] string CallbackUrl,
    [property: JsonPropertyName("cardSave")] bool CardSave,
    [property: JsonPropertyName("operation")] string Operation);

/// <summary><c>POST /api/v3/refund</c> body-si.</summary>
public sealed record PayriffRefundRequest(
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("orderId")] string OrderId,
    [property: JsonPropertyName("refundReason")] string RefundReason);

/// <summary>Bütün cavabların zərfi.</summary>
public sealed class PayriffEnvelope<TPayload>
{
    [JsonPropertyName("code")]
    public string? Code { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("internalMessage")]
    public string? InternalMessage { get; init; }

    [JsonPropertyName("responseId")]
    public string? ResponseId { get; init; }

    [JsonPropertyName("payload")]
    public TPayload? Payload { get; init; }
}

/// <summary>Create Order cavabının <c>payload</c>-u.</summary>
public sealed class PayriffCreateOrderPayload
{
    [JsonPropertyName("orderId")]
    public string? OrderId { get; init; }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }

    [JsonPropertyName("paymentUrl")]
    public string? PaymentUrl { get; init; }

    [JsonPropertyName("transactionId")]
    public long? TransactionId { get; init; }
}

/// <summary>Order Information (<c>GET /api/v3/orders/{orderId}</c>) cavabının <c>payload</c>-u (lazım olan sahələr).</summary>
public sealed class PayriffOrderPayload
{
    [JsonPropertyName("orderId")]
    public string? OrderId { get; init; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; init; }

    [JsonPropertyName("currencyType")]
    public string? CurrencyType { get; init; }

    [JsonPropertyName("operationType")]
    public string? OperationType { get; init; }

    /// <summary><c>PAID</c>, <c>PENDING</c>, <c>FAILED</c> … (bax <see cref="PayriffStatus"/>).</summary>
    [JsonPropertyName("paymentStatus")]
    public string? PaymentStatus { get; init; }

    [JsonPropertyName("createdDate")]
    public DateTimeOffset? CreatedDate { get; init; }
}

/// <summary>Bərabərlik üçün sabitlər.</summary>
public static class PayriffConstants
{
    public const string SuccessCode = "00000";
    public const string CurrencyAzn = "AZN";
    public const string LanguageAz = "AZ";
    public const string OperationPurchase = "PURCHASE";
}
