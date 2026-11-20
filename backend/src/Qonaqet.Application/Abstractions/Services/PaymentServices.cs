namespace Qonaqet.Application.Abstractions.Services;

/// <summary>
/// Onlayn ödəniş provayderi (Payriff, Development-də Fake). Məbləğlər həmişə <c>long</c> qəpikdir; provayderin
/// onluq formatına çevirmə adapterin içindədir. Provayder xətası <see cref="PaymentGatewayException"/> atır.
/// </summary>
public interface IPaymentGateway
{
    /// <summary><c>payments.provider</c> sütununa yazılan sabit ad (<c>Payriff</c>, <c>Fake</c>).</summary>
    string Name { get; }

    /// <summary>Sifariş yaradır; müştəri <see cref="GatewayOrder.PaymentUrl"/>-ə yönləndirilir.</summary>
    Task<GatewayOrder> CreateOrderAsync(GatewayOrderRequest request, CancellationToken cancellationToken);

    /// <summary>Sifarişin provayderdəki həqiqi vəziyyəti (callback imzasızdır, ona görə status yalnız buradan təsdiqlənir).</summary>
    Task<GatewayOrderStatus> GetOrderAsync(string providerOrderId, CancellationToken cancellationToken);

    /// <summary>Tam və ya qismən refund.</summary>
    Task<GatewayRefund> RefundAsync(string providerOrderId, long amountQepik, string reason, CancellationToken cancellationToken);
}

/// <summary>Sifariş sorğusu. <see cref="Description"/> şəxsi məlumat daşımamalıdır.</summary>
public sealed record GatewayOrderRequest(long AmountQepik, string Description, Uri CallbackUrl);

public sealed record GatewayOrder(string ProviderOrderId, Uri PaymentUrl);

/// <summary>Provayder statusunun bizim üç vəziyyətə xəritəsi.</summary>
public enum GatewayPaymentState
{
    /// <summary>Hələ ödənilməyib (yaradılıb, gözləyir).</summary>
    Pending,

    /// <summary>Pul tutulub (sonradan refund olunsa da).</summary>
    Paid,

    /// <summary>Rədd edilib, ləğv olunub və ya provayderdə müddəti bitib.</summary>
    Failed,
}

public sealed record GatewayOrderStatus(string ProviderOrderId, GatewayPaymentState State, long AmountQepik, string ProviderStatus);

/// <summary>Refund nəticəsi; <see cref="ProviderReference"/> uzlaşdırma üçün saxlanılır.</summary>
public sealed record GatewayRefund(string? ProviderReference);

/// <summary>Provayder əlçatmazdır və ya əməliyyatı rədd etdi. Mesajda sirr və ya kart məlumatı olmur.</summary>
public sealed class PaymentGatewayException : Exception
{
    public PaymentGatewayException()
    {
    }

    public PaymentGatewayException(string message)
        : base(message)
    {
    }

    public PaymentGatewayException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public PaymentGatewayException(string message, string? providerCode)
        : base(message)
    {
        ProviderCode = providerCode;
    }

    /// <summary>Provayderin cavab kodu (Payriff: <c>code</c>), varsa.</summary>
    public string? ProviderCode { get; }
}
