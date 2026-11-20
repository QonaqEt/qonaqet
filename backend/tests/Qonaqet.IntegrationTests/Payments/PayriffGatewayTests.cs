using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Infrastructure.Payments;
using Qonaqet.Infrastructure.Payments.Payriff;

namespace Qonaqet.IntegrationTests.Payments;

/// <summary>Payriff v3 klienti: sorğu formatı, cavab zərfi, status xəritəsi, retry siyasəti (saxta HTTP handler ilə).</summary>
public sealed class PayriffGatewayTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_order_sends_the_documented_body_and_secret_header()
    {
        var handler = new StubHandler(_ => Ok("""
            {"code":"00000","message":"Operation performed successfully","payload":{"orderId":"o-1","sessionId":"s","paymentUrl":"https://sbpay.payriff.com/r/o-1?type=direct","transactionId":1152924}}
            """));
        var gateway = Gateway(handler);

        var order = await gateway.CreateOrderAsync(new GatewayOrderRequest(17805, "QONAQET hədiyyə", new Uri("https://qonaqet.az/api/v1/payments/payriff/callback")), Ct);

        Assert.Equal("o-1", order.ProviderOrderId);
        Assert.Equal(new Uri("https://sbpay.payriff.com/r/o-1?type=direct"), order.PaymentUrl);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.payriff.com/api/v3/orders", request.Uri.ToString());
        Assert.Equal("secret-key", request.Authorization); // prefikssiz
        using var body = JsonDocument.Parse(request.Body!);
        var root = body.RootElement;
        Assert.Equal(178.05m, root.GetProperty("amount").GetDecimal());
        Assert.Equal("AZ", root.GetProperty("language").GetString());
        Assert.Equal("AZN", root.GetProperty("currency").GetString());
        Assert.Equal("PURCHASE", root.GetProperty("operation").GetString());
        Assert.Equal("https://qonaqet.az/api/v1/payments/payriff/callback", root.GetProperty("callbackUrl").GetString());
        Assert.False(root.GetProperty("cardSave").GetBoolean());
        Assert.Equal("QONAQET hədiyyə", root.GetProperty("description").GetString());
    }

    [Theory]
    [InlineData("PAID", GatewayPaymentState.Paid)]
    [InlineData("REFUNDED", GatewayPaymentState.Paid)]
    [InlineData("DECLINED", GatewayPaymentState.Failed)]
    [InlineData("CANCELED", GatewayPaymentState.Failed)]
    [InlineData("EXPIRED", GatewayPaymentState.Failed)]
    [InlineData("CREATED", GatewayPaymentState.Pending)]
    [InlineData("PENDING", GatewayPaymentState.Pending)]
    [InlineData("SOMETHING_NEW", GatewayPaymentState.Pending)]
    public async Task Get_order_maps_the_payment_status(string providerStatus, GatewayPaymentState expected)
    {
        var handler = new StubHandler(_ => Ok($$$"""
            {"code":"00000","payload":{"orderId":"o-2","amount":149.99,"currencyType":"AZN","operationType":"PURCHASE","paymentStatus":"{{{providerStatus}}}"}}
            """));

        var status = await Gateway(handler).GetOrderAsync("o-2", Ct);

        Assert.Equal(expected, status.State);
        Assert.Equal(14999, status.AmountQepik);
        Assert.Equal(providerStatus, status.ProviderStatus);
        Assert.Equal("https://api.payriff.com/api/v3/orders/o-2", handler.Requests[0].Uri.ToString());
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
    }

    [Fact]
    public async Task Refund_sends_amount_order_and_reason()
    {
        var handler = new StubHandler(_ => Ok("""{"code":"00000","message":"ok","responseId":"r-77"}"""));

        var refund = await Gateway(handler).RefundAsync("o-3", 601_00, "Hədiyyənin müddəti bitdi", Ct);

        Assert.Equal("r-77", refund.ProviderReference);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.payriff.com/api/v3/refund", request.Uri.ToString());
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal(601.00m, body.RootElement.GetProperty("amount").GetDecimal());
        Assert.Equal("o-3", body.RootElement.GetProperty("orderId").GetString());
        Assert.Equal("Hədiyyənin müddəti bitdi", body.RootElement.GetProperty("refundReason").GetString());
    }

    [Fact]
    public async Task Non_success_code_throws_a_gateway_exception_with_the_provider_code()
    {
        var handler = new StubHandler(_ => Ok("""{"code":"01000","message":"Insufficient amount"}"""));

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() => Gateway(handler).RefundAsync("o-4", 100, "x", Ct));

        Assert.Equal("01000", ex.ProviderCode);
    }

    [Fact]
    public async Task Missing_app_key_fails_without_calling_payriff()
    {
        var handler = new StubHandler(_ => Ok("{}"));
        var gateway = new PayriffGateway(new HttpClient(handler) { BaseAddress = new Uri("https://api.payriff.com") }, Options.Create(new PayriffOptions()));

        await Assert.ThrowsAsync<PaymentGatewayException>(() => gateway.GetOrderAsync("o", Ct));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(1, 0.01)]
    [InlineData(17805, 178.05)]
    [InlineData(200_00, 200)]
    public void Qepik_and_decimal_amounts_round_trip(long qepik, double amount)
    {
        Assert.Equal((decimal)amount, PayriffGateway.ToAmount(qepik));
        Assert.Equal(qepik, PayriffGateway.ToQepik((decimal)amount));
    }

    [Fact]
    public async Task Resilience_retries_get_but_never_retries_post()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await using var provider = Services(handler);
        var gateway = provider.CreateScope().ServiceProvider.GetRequiredService<PayriffGateway>();
        Assert.Equal(PayriffGateway.ProviderName, gateway.Name);

        await Assert.ThrowsAsync<PaymentGatewayException>(() => gateway.CreateOrderAsync(new GatewayOrderRequest(100, "x", new Uri("https://a.az/cb")), Ct));
        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post));

        await Assert.ThrowsAsync<PaymentGatewayException>(() => gateway.RefundAsync("o", 100, "x", Ct));
        Assert.Equal(2, handler.Requests.Count(r => r.Method == HttpMethod.Post));

        await Assert.ThrowsAsync<PaymentGatewayException>(() => gateway.GetOrderAsync("o", Ct));
        Assert.True(handler.Requests.Count(r => r.Method == HttpMethod.Get) > 1, "GET transient xətada təkrarlanmalıdır");
    }

    private static PayriffGateway Gateway(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.payriff.com") }, Options.Create(new PayriffOptions { AppKey = "secret-key" }));

    private static ServiceProvider Services(StubHandler handler)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payriff:AppKey"] = "secret-key",
            ["Payriff:RetryDelay"] = "00:00:00.010",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPayments(configuration);
        services.AddHttpClient<PayriffGateway>().ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? Body);

    internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private readonly List<RecordedRequest> _requests = [];

        public IReadOnlyList<RecordedRequest> Requests
        {
            get
            {
                lock (_requests)
                {
                    return [.. _requests];
                }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            lock (_requests)
            {
                _requests.Add(new RecordedRequest(request.Method, request.RequestUri!, request.Headers.TryGetValues("Authorization", out var values) ? values.Single() : null, body));
            }

            return respond(request);
        }
    }
}
