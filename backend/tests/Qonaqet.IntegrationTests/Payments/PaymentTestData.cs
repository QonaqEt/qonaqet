using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Payments.Common;
using Qonaqet.Domain.Payments;
using Qonaqet.Infrastructure.Messaging.Inbox;
using Qonaqet.Infrastructure.Messaging.Outbox;
using Qonaqet.Infrastructure.Persistence.Postgres;
using MongoDB.Driver;
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

    /// <summary>
    /// Relay + broker əvəzi: hər iki outbox-dakı (Postgres və Mongo) consumer-in dinlədiyi hadisələri inbox ilə emal edir (sıra ilə). Emal
    /// olunan mesajların sayını qaytarır; təkrar çağırış inbox sayəsində heç nə etmir.
    /// </summary>
    public static async Task<int> DeliverAsync(ApiTestHost host, IIntegrationEventConsumer consumer)
    {
        var messages = new List<(DateTimeOffset At, IntegrationMessage Message)>();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
            var types = consumer.EventTypes.ToList();
            var pg = await context.OutboxMessages.AsNoTracking().Where(m => types.Contains(m.Type)).ToListAsync(Ct);
            messages.AddRange(pg.Select(m => (m.OccurredAt, new IntegrationMessage(m.Id, m.Type, m.Payload))));
            var mongo = await host.Database.GetCollection<OutboxMessage>(MongoOutbox.CollectionName)
                .Find(Builders<OutboxMessage>.Filter.In(m => m.Type, types)).ToListAsync(Ct);
            messages.AddRange(mongo.Select(m => (m.OccurredAt, new IntegrationMessage(m.Id, m.Type, m.Payload))));
        }

        var processed = 0;
        foreach (var (_, message) in messages.OrderBy(m => m.At))
        {
            await using var scope = host.Services.CreateAsyncScope();
            if (await new InboxProcessor(scope.ServiceProvider.GetRequiredService<QonaqetDbContext>()).ProcessAsync(consumer, message, host.Clock.UtcNow, Ct))
            {
                processed++;
            }
        }

        return processed;
    }

    public static Infrastructure.Messaging.Consumers.PaymentFulfillmentConsumer Fulfillment(ApiTestHost host) =>
        new(host.Services.GetRequiredService<IServiceScopeFactory>());

    public static Infrastructure.Messaging.Consumers.LedgerConsumer Ledger() =>
        new(Microsoft.Extensions.Options.Options.Create(new Infrastructure.Messaging.Consumers.LedgerOptions()));

    /// <summary>Kassada fake ödəniş səhifəsində "Ödə" (callback simulyasiyası ilə).</summary>
    public static async Task PayAsync(ApiTestHost host, string paymentUrl, bool paid = true)
    {
        using var response = await host.GetAsync(new Uri(paymentUrl).PathAndQuery + (paid ? "?result=paid" : "?result=failed"), null);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    public static async Task<HttpResponseMessage> PostIdempotentAsync(ApiTestHost host, string path, string token, object body, string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return await host.Client.SendAsync(request, Ct);
    }
}
