using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Qonaqet.Application.Abstractions.Services;

namespace Qonaqet.Infrastructure.Payments.Payriff;

/// <summary>Payriff parametrləri (<c>Payriff</c> bölməsi). <see cref="AppKey"/> yalnız konfiqurasiyadan/mühitdən gəlir.</summary>
public sealed class PayriffOptions
{
    public const string SectionName = "Payriff";

    public Uri BaseUrl { get; set; } = new("https://api.payriff.com");

    /// <summary>Merchant secret key: <c>Authorization</c> başlığına prefikssiz yazılır.</summary>
    public string AppKey { get; set; } = string.Empty;

    /// <summary>Bir cəhdin taymautu.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Retry-lar daxil ümumi taymaut.</summary>
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>GET sorğularının retry-ları arasında baza gecikmə (eksponensial, jitter ilə).</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
}

/// <summary>
/// Payriff Gateway API v3 klienti (<c>HttpClient</c> üzərində öz kodumuz). Retry/timeout
/// <c>Microsoft.Extensions.Http.Resilience</c> ilə qurulur; təhlükəsiz olmayan metodlar (POST: sifariş, refund) heç vaxt
/// təkrarlanmır — ikiqat refund və ya ikiqat sifariş riski olmasın.
/// </summary>
public sealed class PayriffGateway(HttpClient http, IOptions<PayriffOptions> options) : IPaymentGateway
{
    public const string ProviderName = "Payriff";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Name => ProviderName;

    public async Task<GatewayOrder> CreateOrderAsync(GatewayOrderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = new PayriffCreateOrderRequest(
            ToAmount(request.AmountQepik),
            PayriffConstants.LanguageAz,
            PayriffConstants.CurrencyAzn,
            request.Description,
            request.CallbackUrl.ToString(),
            CardSave: false,
            PayriffConstants.OperationPurchase);

        var payload = await SendAsync<PayriffCreateOrderPayload>(HttpMethod.Post, "api/v3/orders", body, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(payload?.OrderId) || !Uri.TryCreate(payload.PaymentUrl, UriKind.Absolute, out var paymentUrl))
        {
            throw new PaymentGatewayException("Payriff sifariş cavabında orderId/paymentUrl yoxdur.");
        }

        return new GatewayOrder(payload.OrderId, paymentUrl);
    }

    public async Task<GatewayOrderStatus> GetOrderAsync(string providerOrderId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerOrderId);
        var payload = await SendAsync<PayriffOrderPayload>(
            HttpMethod.Get, "api/v3/orders/" + Uri.EscapeDataString(providerOrderId), body: null, cancellationToken).ConfigureAwait(false)
            ?? throw new PaymentGatewayException("Payriff sifariş məlumatı boşdur.");

        var status = payload.PaymentStatus ?? string.Empty;
        return new GatewayOrderStatus(payload.OrderId ?? providerOrderId, PayriffStatus.Map(status), ToQepik(payload.Amount), status);
    }

    public async Task<GatewayRefund> RefundAsync(string providerOrderId, long amountQepik, string reason, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerOrderId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountQepik);
        var envelope = await SendEnvelopeAsync<JsonElement?>(
            HttpMethod.Post, "api/v3/refund", new PayriffRefundRequest(ToAmount(amountQepik), providerOrderId, reason), cancellationToken).ConfigureAwait(false);
        return new GatewayRefund(envelope.ResponseId);
    }

    /// <summary>Qəpik → Payriff onluq məbləği (2 rəqəm).</summary>
    public static decimal ToAmount(long qepik) => decimal.Round(qepik / 100m, 2);

    /// <summary>Payriff onluq məbləği → qəpik.</summary>
    public static long ToQepik(decimal amount) => (long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);

    private async Task<TPayload?> SendAsync<TPayload>(HttpMethod method, string path, object? body, CancellationToken cancellationToken) =>
        (await SendEnvelopeAsync<TPayload>(method, path, body, cancellationToken).ConfigureAwait(false)).Payload;

    private async Task<PayriffEnvelope<TPayload>> SendEnvelopeAsync<TPayload>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.AppKey))
        {
            throw new PaymentGatewayException("Payriff:AppKey konfiqurasiya olunmayıb.");
        }

        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.TryAddWithoutValidation("Authorization", options.Value.AppKey);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new PaymentGatewayException("Payriff əlçatan deyil.", ex);
        }

        using (response)
        {
            PayriffEnvelope<TPayload>? envelope = null;
            try
            {
                envelope = await response.Content.ReadFromJsonAsync<PayriffEnvelope<TPayload>>(Json, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                // Aşağıda HTTP statusu ilə birlikdə xəta verilir.
            }

            if (!response.IsSuccessStatusCode || envelope is null || envelope.Code != PayriffConstants.SuccessCode)
            {
                throw new PaymentGatewayException(
                    string.Create(CultureInfo.InvariantCulture, $"Payriff əməliyyatı rədd etdi (HTTP {(int)response.StatusCode}, code {envelope?.Code ?? "-"}): {envelope?.Message}"),
                    envelope?.Code);
            }

            return envelope;
        }
    }
}

/// <summary>Payriff <c>paymentStatus</c> dəyərlərinin xəritəsi.</summary>
public static class PayriffStatus
{
    private static readonly HashSet<string> PaidStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "PAID", "APPROVED", "REFUNDED", "PARTIALLY_REFUNDED", "PARTIAL_REFUNDED",
    };

    private static readonly HashSet<string> FailedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "FAILED", "DECLINED", "CANCELED", "CANCELLED", "EXPIRED", "REJECTED", "REVERSED", "ERROR",
    };

    /// <summary>
    /// <c>PAID</c> (sonradan refund olunmuş daxil) → <see cref="GatewayPaymentState.Paid"/>; rədd/ləğv/müddət → <see cref="GatewayPaymentState.Failed"/>;
    /// qalan hər şey (<c>CREATED</c>, <c>PENDING</c>, naməlum yeni dəyərlər) → <see cref="GatewayPaymentState.Pending"/>, yəni yenidən yoxlanılır.
    /// </summary>
    public static GatewayPaymentState Map(string status)
    {
        if (PaidStatuses.Contains(status))
        {
            return GatewayPaymentState.Paid;
        }

        return FailedStatuses.Contains(status) ? GatewayPaymentState.Failed : GatewayPaymentState.Pending;
    }
}
