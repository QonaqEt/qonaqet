using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Payments.Fake;

/// <summary>
/// Fake sifariş (<c>fakePaymentOrders</c>). Sifarişlər bütün host-lar (Api, Worker, Integration.Api) üçün görünsün deyə Mongo-da
/// saxlanılır, yaddaşda yox. Heç vaxt silinmir.
/// </summary>
public sealed class FakePaymentOrder
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    public long AmountQepik { get; set; }

    public string Description { get; set; } = string.Empty;

    public GatewayPaymentState State { get; set; }

    public long RefundedQepik { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public bool IsDeleted { get; set; }
}

/// <summary>
/// Payriff-in Development/sandbox əvəzi: real pul hərəkəti yoxdur. Ödəniş səhifəsi <c>GET api/v1/payments/fake/{orderId}</c>-dir və
/// ödənişi uğurlu və ya uğursuz edə bilir; status, refund və xəta davranışı Payriff klienti ilə eynidir.
/// </summary>
public sealed class FakePaymentGateway(IMongoDatabase database, IConfiguration configuration, IHostEnvironment environment, IClock clock)
    : IPaymentGateway, IFakeCheckout
{
    public const string ProviderName = "Fake";
    public const string CollectionName = "fakePaymentOrders";
    public const string CheckoutPath = "api/v1/payments/fake/";

    private readonly IMongoCollection<FakePaymentOrder> _orders = database.GetCollection<FakePaymentOrder>(CollectionName);

    public string Name => ProviderName;

    /// <summary>Ödəniş səhifəsi yalnız Development-də açıqdır (sandbox partnyorları isə API ilə işləyir).</summary>
    public bool IsEnabled => environment.IsDevelopment();

    public async Task<GatewayOrder> CreateOrderAsync(GatewayOrderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.AmountQepik);
        var order = new FakePaymentOrder
        {
            Id = "fake-" + Guid.CreateVersion7().ToString("N"),
            AmountQepik = request.AmountQepik,
            Description = request.Description,
            State = GatewayPaymentState.Pending,
            CreatedAt = clock.UtcNow,
        };
        await _orders.InsertOneAsync(order, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new GatewayOrder(order.Id, new Uri(BaseUrl(configuration), CheckoutPath + order.Id));
    }

    public async Task<GatewayOrderStatus> GetOrderAsync(string providerOrderId, CancellationToken cancellationToken)
    {
        var order = await LoadAsync(providerOrderId, cancellationToken).ConfigureAwait(false)
            ?? throw new PaymentGatewayException("Fake sifariş tapılmadı.", "NOT_FOUND");
        return new GatewayOrderStatus(order.Id, order.State, order.AmountQepik, order.State.ToString().ToUpperInvariant());
    }

    public async Task<GatewayRefund> RefundAsync(string providerOrderId, long amountQepik, string reason, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountQepik);
        var order = await LoadAsync(providerOrderId, cancellationToken).ConfigureAwait(false)
            ?? throw new PaymentGatewayException("Fake sifariş tapılmadı.", "NOT_FOUND");
        if (order.State != GatewayPaymentState.Paid || order.RefundedQepik + amountQepik > order.AmountQepik)
        {
            throw new PaymentGatewayException("Refund məbləği ödənişi aşır və ya ödəniş tamamlanmayıb.", "REFUND_REJECTED");
        }

        // Şərtli artım: eyni anda iki refund ödənişi aşa bilməz.
        var f = Builders<FakePaymentOrder>.Filter;
        var updated = await _orders.UpdateOneAsync(
            f.Eq(o => o.Id, order.Id) & f.Eq(o => o.RefundedQepik, order.RefundedQepik),
            Builders<FakePaymentOrder>.Update.Inc(o => o.RefundedQepik, amountQepik),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (updated.ModifiedCount == 0)
        {
            throw new PaymentGatewayException("Fake sifariş eyni anda dəyişdi.", "CONFLICT");
        }

        return new GatewayRefund("fake-refund-" + Guid.CreateVersion7().ToString("N"));
    }

    public async Task<FakeOrderView?> FindAsync(string providerOrderId, CancellationToken cancellationToken)
    {
        var order = await LoadAsync(providerOrderId, cancellationToken).ConfigureAwait(false);
        return order is null ? null : new FakeOrderView(order.Id, order.AmountQepik, order.Description, order.State, order.RefundedQepik);
    }

    public async Task<bool> CompleteAsync(string providerOrderId, bool paid, CancellationToken cancellationToken)
    {
        var f = Builders<FakePaymentOrder>.Filter;
        var result = await _orders.UpdateOneAsync(
            f.Eq(o => o.Id, providerOrderId) & f.Eq(o => o.State, GatewayPaymentState.Pending),
            Builders<FakePaymentOrder>.Update
                .Set(o => o.State, paid ? GatewayPaymentState.Paid : GatewayPaymentState.Failed)
                .Set(o => o.CompletedAt, clock.UtcNow),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.ModifiedCount == 1;
    }

    /// <summary>Fake ödəniş səhifəsinin bazası (<c>App:PublicBaseUrl</c>; yoxdursa lokal Api).</summary>
    public static Uri BaseUrl(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var value = configuration["App:PublicBaseUrl"];
        var url = string.IsNullOrWhiteSpace(value) ? "http://localhost:5080" : value;
        return new Uri(url.EndsWith('/') ? url : url + "/");
    }

    private Task<FakePaymentOrder?> LoadAsync(string providerOrderId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerOrderId);
        return _orders.Find(o => o.Id == providerOrderId && !o.IsDeleted).FirstOrDefaultAsync(cancellationToken)!;
    }
}
