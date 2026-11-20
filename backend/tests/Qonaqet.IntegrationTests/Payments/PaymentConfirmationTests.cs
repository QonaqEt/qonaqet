using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Payments.Jobs;
using Qonaqet.Domain.Payments;
using Qonaqet.IntegrationTests.Identity;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Payments;

/// <summary>Callback (imzasız, yalnız tetikleyici) və 5 dəqiqəlik yenidən yoxlama job-u (E07-T04).</summary>
[Collection(PaymentsCollection.Name)]
public sealed class PaymentConfirmationTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Callback_confirms_through_get_order_and_writes_the_event_once()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var started = await StartAsync(host, Guid.CreateVersion7());
        var orderId = (await LoadAsync(host, started.PaymentId)).ProviderOrderId;
        await CompleteAtProviderAsync(host, orderId, paid: true);

        using (var callback = await host.PostAsync("/api/v1/payments/payriff/callback", new { code = "00000", payload = new { orderId } }))
        {
            Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        }

        using (var again = await host.PostAsync("/api/v1/payments/payriff/callback", new { orderId }))
        {
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        }

        var payment = await LoadAsync(host, started.PaymentId);
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.NotNull(payment.PaidAt);
        Assert.Equal(["PaymentConfirmed"], await OutboxTypesAsync(host));
    }

    [Fact]
    public async Task Forged_or_unknown_callbacks_change_nothing()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var started = await StartAsync(host, Guid.CreateVersion7());
        var orderId = (await LoadAsync(host, started.PaymentId)).ProviderOrderId;

        // Provayderdə ödənilməyib: "PAID" deyən saxta callback heç nə dəyişmir.
        using (var forged = await host.PostAsync("/api/v1/payments/payriff/callback", new { orderId, paymentStatus = "PAID" }))
        {
            Assert.Equal(HttpStatusCode.OK, forged.StatusCode);
        }

        using (var unknown = await host.PostAsync("/api/v1/payments/payriff/callback", new { orderId = "no-such-order" }))
        {
            Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        }

        using (var empty = await host.PostAsync("/api/v1/payments/payriff/callback", new { }))
        {
            Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        }

        Assert.Equal(PaymentStatus.Pending, (await LoadAsync(host, started.PaymentId)).Status);
        Assert.Empty(await OutboxTypesAsync(host));
    }

    [Fact]
    public async Task Fake_checkout_simulates_the_callback()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var payer = await host.LoginAsync("fake-payer@example.az");
        var started = await StartAsync(host, UserId(payer));

        using (var pay = await host.GetAsync(started.PaymentUrl.PathAndQuery + "?result=paid", null))
        {
            Assert.Equal(HttpStatusCode.OK, pay.StatusCode);
        }

        using var status = await host.GetAsync($"/api/v1/payments/{started.PaymentId.Value}", Access(payer));
        var json = await host.JsonAsync(status);
        Assert.Equal("Paid", json.GetProperty("status").GetString());
        Assert.False(json.TryGetProperty("paymentUrl", out var url) && url.ValueKind != System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Recheck_job_confirms_lost_callbacks_fails_declined_and_expires_stale_payments()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var start = host.Clock.UtcNow;

        var lostCallback = await StartAsync(host, Guid.CreateVersion7());
        var declined = await StartAsync(host, Guid.CreateVersion7());
        var abandoned = await StartAsync(host, Guid.CreateVersion7());
        await CompleteAtProviderAsync(host, (await LoadAsync(host, lostCallback.PaymentId)).ProviderOrderId, paid: true);
        await CompleteAtProviderAsync(host, (await LoadAsync(host, declined.PaymentId)).ProviderOrderId, paid: false);

        // 1 dəqiqə: hələ callback-in vaxtıdır, job toxunmur.
        host.Clock.UtcNow = start.AddMinutes(1);
        Assert.Equal(0, await RunJobAsync(host));
        Assert.Equal(PaymentStatus.Pending, (await LoadAsync(host, lostCallback.PaymentId)).Status);

        // 3 dəqiqə: provayderin dediyi tətbiq olunur; ödənilməmiş sifariş hələ gözləyir.
        var fresh = await StartAsync(host, Guid.CreateVersion7()); // start + 1 dəq-də yaradılıb
        host.Clock.UtcNow = start.AddMinutes(3);
        Assert.Equal(2, await RunJobAsync(host));
        Assert.Equal(PaymentStatus.Paid, (await LoadAsync(host, lostCallback.PaymentId)).Status);
        Assert.Equal(PaymentStatus.Failed, (await LoadAsync(host, declined.PaymentId)).Status);
        Assert.Equal(PaymentStatus.Pending, (await LoadAsync(host, abandoned.PaymentId)).Status);

        // 30 dəqiqə: ödənilməmiş sifariş bitir, təzəsi (29 dəqiqəlik) hələ yox.
        host.Clock.UtcNow = start.AddMinutes(30);
        Assert.Equal(1, await RunJobAsync(host));
        Assert.Equal(PaymentStatus.Expired, (await LoadAsync(host, abandoned.PaymentId)).Status);
        Assert.Equal(PaymentStatus.Pending, (await LoadAsync(host, fresh.PaymentId)).Status);

        Assert.Equal(0, await RunJobAsync(host));
        Assert.Equal(["PaymentClosedUnpaid", "PaymentClosedUnpaid", "PaymentConfirmed"], (await OutboxTypesAsync(host)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Late_payment_after_expiry_is_still_confirmed()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var start = host.Clock.UtcNow;
        var started = await StartAsync(host, Guid.CreateVersion7());
        host.Clock.UtcNow = start.AddMinutes(31);
        await RunJobAsync(host);
        Assert.Equal(PaymentStatus.Expired, (await LoadAsync(host, started.PaymentId)).Status);

        var orderId = (await LoadAsync(host, started.PaymentId)).ProviderOrderId;
        await CompleteAtProviderAsync(host, orderId, paid: true);
        using (await host.PostAsync("/api/v1/payments/payriff/callback", new { orderId }))
        {
        }

        Assert.Equal(PaymentStatus.Paid, (await LoadAsync(host, started.PaymentId)).Status);
    }

    [Fact]
    public async Task Callbacks_racing_the_job_confirm_exactly_once()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var start = host.Clock.UtcNow;
        var started = await StartAsync(host, Guid.CreateVersion7());
        var orderId = (await LoadAsync(host, started.PaymentId)).ProviderOrderId;
        await CompleteAtProviderAsync(host, orderId, paid: true);
        host.Clock.UtcNow = start.AddMinutes(3);

        var callbacks = Enumerable.Range(0, 6).Select(async _ =>
        {
            using var response = await host.PostAsync("/api/v1/payments/payriff/callback", new { orderId });
            return response.StatusCode;
        });
        var results = await Task.WhenAll(callbacks.Append(RunJobAsync(host).ContinueWith(_ => HttpStatusCode.OK, TaskScheduler.Default)));

        Assert.All(results, s => Assert.Equal(HttpStatusCode.OK, s));
        Assert.Equal(PaymentStatus.Paid, (await LoadAsync(host, started.PaymentId)).Status);
        Assert.Equal(["PaymentConfirmed"], await OutboxTypesAsync(host));
    }

    [Fact]
    public async Task Callback_is_rate_limited()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        host.RateLimits.Deny = true;

        using var response = await host.PostAsync("/api/v1/payments/payriff/callback", new { orderId = "x" });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    private static async Task CompleteAtProviderAsync(ApiTestHost host, string orderId, bool paid)
    {
        await using var scope = host.Services.CreateAsyncScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<IFakeCheckout>().CompleteAsync(orderId, paid, Ct));
    }

    private static async Task<int> RunJobAsync(ApiTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await ActivatorUtilities.CreateInstance<RecheckPaymentsJob>(scope.ServiceProvider).RunAsync(Ct);
    }
}
