using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Payments.Common;
using Qonaqet.Domain.Payments;
using Qonaqet.Infrastructure.Persistence.Postgres;
using Qonaqet.IntegrationTests.Identity;

namespace Qonaqet.IntegrationTests.Payments;

/// <summary>Ödəniş testlərinin ortaq addımları.</summary>
internal static class PaymentTestData
{
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static string Access(JsonElement login) => login.GetProperty("tokens").GetProperty("accessToken").GetString()!;

    public static Guid UserId(JsonElement login) => Guid.Parse(login.GetProperty("userId").GetString()!);

    /// <summary>Məqsəd əmrinin edəcəyi kimi ödəniş başladır (Fake provayder).</summary>
    public static async Task<StartedPayment> StartAsync(ApiTestHost host, Guid? payer, long amount = 25_00, PaymentPurpose purpose = PaymentPurpose.Gift, Guid? reference = null)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<PaymentStarter>()
            .StartAsync(new PaymentStart(purpose, reference ?? Guid.CreateVersion7(), amount, "QONAQET test", new PaymentPayer(payer)), Ct);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    /// <summary>Provayderdə ödənişi tamamlayır və bizim ödənişi birbaşa <c>Paid</c> edir (callback axını E07-T04 testlərindədir).</summary>
    public static async Task MarkPaidAsync(ApiTestHost host, PaymentId id)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
        var payment = (await repository.GetAsync(id, Ct))!;
        Assert.True(await scope.ServiceProvider.GetRequiredService<IFakeCheckout>().CompleteAsync(payment.ProviderOrderId, paid: true, Ct));
        Assert.True(payment.MarkPaid(host.Clock.UtcNow, "PAID"));
        await repository.SaveChangesAsync(Ct);
    }

    public static async Task<Payment> LoadAsync(ApiTestHost host, PaymentId id)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IPaymentRepository>().GetAsync(id, Ct))!;
    }

    /// <summary>Postgres outbox-undakı hadisə tipləri (qısa ad), yazılma sırası ilə.</summary>
    public static async Task<IReadOnlyList<string>> OutboxTypesAsync(ApiTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
        var types = await context.OutboxMessages.AsNoTracking().OrderBy(m => m.OccurredAt).Select(m => m.Type).ToListAsync(Ct);
        return types.Select(t => t[(t.LastIndexOf('.') + 1)..]).ToArray();
    }

    public static async Task<HttpResponseMessage> RefundAsync(ApiTestHost host, string adminToken, PaymentId id, long? amount, string? key = "auto")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/v1/admin/payments/{id.Value}/refunds", UriKind.Relative))
        {
            Content = JsonContent.Create(new { amountQepik = amount, reason = "Müştəri müraciəti" }),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key == "auto" ? Guid.NewGuid().ToString() : key);
        }

        return await host.Client.SendAsync(request, Ct);
    }
}
