using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Infrastructure.Payments;
using Qonaqet.Infrastructure.Payments.Fake;
using Qonaqet.Infrastructure.Payments.Payriff;
using Qonaqet.IntegrationTests.Identity;

namespace Qonaqet.IntegrationTests.Payments;

/// <summary>Fake gateway (səhifə ödənişdən sonra callback-i simulyasiya edir, ona görə Postgres də lazımdır): ödəniş səhifəsi (yalnız Development), status/refund davranışı, provayder seçimi.</summary>
[Collection(PaymentsCollection.Name)]
public sealed class FakeGatewayTests(PaymentsFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Checkout_page_marks_the_order_paid_once()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var order = await CreateOrderAsync(host, 12_50);
        Assert.StartsWith("http://localhost:5080/api/v1/payments/fake/fake-", order.PaymentUrl.ToString(), StringComparison.Ordinal);

        using (var page = await host.GetAsync(order.PaymentUrl.PathAndQuery, null))
        {
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            var html = await page.Content.ReadAsStringAsync(Ct);
            Assert.Contains("12.50 AZN", html, StringComparison.Ordinal);
            Assert.Contains("?result=paid", html, StringComparison.Ordinal);
        }

        Assert.Equal(GatewayPaymentState.Pending, (await StatusAsync(host, order.ProviderOrderId)).State);

        using (var pay = await host.GetAsync(order.PaymentUrl.PathAndQuery + "?result=paid", null))
        {
            Assert.Equal(HttpStatusCode.OK, pay.StatusCode);
        }

        var status = await StatusAsync(host, order.ProviderOrderId);
        Assert.Equal(GatewayPaymentState.Paid, status.State);
        Assert.Equal(12_50, status.AmountQepik);

        using var again = await host.GetAsync(order.PaymentUrl.PathAndQuery + "?result=failed", null);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(GatewayPaymentState.Paid, (await StatusAsync(host, order.ProviderOrderId)).State);
    }

    [Fact]
    public async Task Checkout_page_can_fail_the_order()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var order = await CreateOrderAsync(host, 500);

        using var fail = await host.GetAsync(order.PaymentUrl.PathAndQuery + "?result=failed", null);

        Assert.Equal(HttpStatusCode.OK, fail.StatusCode);
        Assert.Equal(GatewayPaymentState.Failed, (await StatusAsync(host, order.ProviderOrderId)).State);
    }

    [Fact]
    public async Task Refunds_never_exceed_the_paid_amount()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var order = await CreateOrderAsync(host, 10_00);
        await using var scope = host.Services.CreateAsyncScope();
        var fake = scope.ServiceProvider.GetRequiredService<FakePaymentGateway>();

        await Assert.ThrowsAsync<PaymentGatewayException>(() => fake.RefundAsync(order.ProviderOrderId, 100, "x", Ct)); // ödənilməyib
        Assert.True(await fake.CompleteAsync(order.ProviderOrderId, paid: true, Ct));

        Assert.NotNull((await fake.RefundAsync(order.ProviderOrderId, 6_00, "qismən", Ct)).ProviderReference);
        await Assert.ThrowsAsync<PaymentGatewayException>(() => fake.RefundAsync(order.ProviderOrderId, 4_01, "çox", Ct));
        await fake.RefundAsync(order.ProviderOrderId, 4_00, "qalan", Ct);

        Assert.Equal(10_00, (await fake.FindAsync(order.ProviderOrderId, Ct))!.RefundedQepik);
        Assert.Equal(GatewayPaymentState.Paid, (await fake.GetOrderAsync(order.ProviderOrderId, Ct)).State);
    }

    [Fact]
    public async Task Checkout_page_is_not_found_outside_development()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync("Staging");
        await using var scope = host.Services.CreateAsyncScope();

        // Sandbox (partnyorlar) hər mühitdə Fake-dir; səhifə isə yalnız Development-də açılır.
        var sandbox = scope.ServiceProvider.GetRequiredService<IPaymentGatewayRegistry>().Sandbox;
        var order = await sandbox.CreateOrderAsync(new GatewayOrderRequest(100, "sandbox", new Uri("https://a.az/cb")), Ct);

        using var page = await host.GetAsync(order.PaymentUrl.PathAndQuery + "?result=paid", null);

        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        Assert.Equal(GatewayPaymentState.Pending, (await sandbox.GetOrderAsync(order.ProviderOrderId, Ct)).State);
    }

    [Theory]
    [InlineData("Development", null, null, FakePaymentGateway.ProviderName)]
    [InlineData("Development", null, "key", PayriffGateway.ProviderName)]
    [InlineData("Development", "Payriff", null, PayriffGateway.ProviderName)]
    [InlineData("Development", "Fake", "key", FakePaymentGateway.ProviderName)]
    [InlineData("Production", null, null, PayriffGateway.ProviderName)]
    [InlineData("Staging", null, "key", PayriffGateway.ProviderName)]
    public void Default_provider_selection(string environment, string? provider, string? appKey, string expected)
    {
        Assert.Equal(expected == FakePaymentGateway.ProviderName, PaymentGatewayRegistry.UseFake(Config(provider, appKey), new Env(environment)));
    }

    [Fact]
    public void Fake_is_never_the_default_outside_development()
    {
        Assert.Throws<InvalidOperationException>(() => PaymentGatewayRegistry.UseFake(Config("Fake", null), new Env("Production")));
    }

    private static async Task<GatewayOrder> CreateOrderAsync(ApiTestHost host, long amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var gateway = scope.ServiceProvider.GetRequiredService<IPaymentGateway>();
        Assert.Equal(FakePaymentGateway.ProviderName, gateway.Name); // Development + boş AppKey
        return await gateway.CreateOrderAsync(new GatewayOrderRequest(amount, "Test hədiyyə", new Uri("https://a.az/cb")), Ct);
    }

    private static async Task<GatewayOrderStatus> StatusAsync(ApiTestHost host, string orderId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPaymentGatewayRegistry>().Get(FakePaymentGateway.ProviderName).GetOrderAsync(orderId, Ct);
    }

    private static IConfiguration Config(string? provider, string? appKey) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { [PaymentsRegistration.ProviderKey] = provider, ["Payriff:AppKey"] = appKey })
        .Build();

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "test";

        public string ContentRootPath { get; set; } = ".";

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
