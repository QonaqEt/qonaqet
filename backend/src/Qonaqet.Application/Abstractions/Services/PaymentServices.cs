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

/// <summary>
/// Provayderlərin seçimi. <see cref="Default"/> yeni ödənişlər üçündür (Payriff; Development-də Fake), <see cref="Sandbox"/> sandbox
/// partnyorları üçün həmişə Fake-dir. Mövcud ödənişin statusu onu yaradan provayderdən (<c>payments.provider</c>) oxunur.
/// </summary>
public interface IPaymentGatewayRegistry
{
    IPaymentGateway Default { get; }

    IPaymentGateway Sandbox { get; }

    /// <summary>Ada görə provayder; tanınmayan ad <see cref="InvalidOperationException"/> atır.</summary>
    IPaymentGateway Get(string providerName);
}

/// <summary>Fake gateway-in ödəniş səhifəsi (yalnız Development-də aktiv; başqa mühitdə <see cref="IsEnabled"/> = false).</summary>
public interface IFakeCheckout
{
    bool IsEnabled { get; }

    /// <summary>Fake sifarişin vəziyyəti; tapılmasa <c>null</c>.</summary>
    Task<FakeOrderView?> FindAsync(string providerOrderId, CancellationToken cancellationToken);

    /// <summary>Gözləyən sifarişi ödənilmiş və ya uğursuz edir; artıq yekunlaşmışsa <c>false</c>.</summary>
    Task<bool> CompleteAsync(string providerOrderId, bool paid, CancellationToken cancellationToken);
}

public sealed record FakeOrderView(string ProviderOrderId, long AmountQepik, string Description, GatewayPaymentState State, long RefundedQepik);

/// <summary>İctimai URL-lər (<c>App:PublicBaseUrl</c> əsasında).</summary>
public interface IAppUrls
{
    /// <summary>Payriff callback-i: <c>{PublicBaseUrl}/api/v1/payments/payriff/callback</c>.</summary>
    Uri PaymentCallback { get; }

    /// <summary>İctimai hədiyyə səhifəsi: <c>{PublicBaseUrl}/g/{token}</c> (token bearer-dir, loglanmır).</summary>
    Uri GiftLink(string token);
}
