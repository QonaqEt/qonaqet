using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Integration.Jobs;
using Qonaqet.Domain.Integration;
using Qonaqet.Domain.Ledger;
using Qonaqet.Infrastructure.Messaging.Consumers;
using Qonaqet.Infrastructure.Messaging.Inbox;
using Qonaqet.Infrastructure.Persistence.Postgres;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Payments;
using Qonaqet.SharedKernel;
using static Qonaqet.IntegrationTests.Payments.LedgerTests;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Partner;

/// <summary>
/// Webhook-lar (E13-T08): abunə (secret bir dəfə), hadisə → jurnalda <c>Pending</c> (təkrar emalda ikinci sətir yox), imzalı göndəriş,
/// uğursuzluqda 1 dəqiqəlik gözləmə, uğur, əl ilə yenidən göndərmə və soft delete.
/// </summary>
[Collection(PaymentsCollection.Name)]
public sealed class PartnerWebhookTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Event_is_signed_delivered_retried_and_redelivered()
    {
        fixture.SkipIfUnavailable();
        await using var api = await fixture.HostAsync();
        await using var partner = new PartnerTestHost(api);
        var businessId = Guid.CreateVersion7();
        var (_, client, secret) = await PartnerTestData.ClientAsync(api, "pwh", [PartnerScopes.Webhooks], businessIds: [businessId]);
        var access = await partner.AccessTokenAsync(client.GetProperty("clientId").GetString()!, secret);

        using (var insecure = await partner.SendAsync(
                   HttpMethod.Post, "/partner/v1/webhooks", access, new { url = "http://hooks.example.az/q", eventTypes = new[] { "payout.created" } }, "pwh-0"))
        {
            Assert.Equal("webhook_url_invalid", (await api.JsonAsync(insecure)).GetProperty("code").GetString());
        }

        string signingSecret;
        Guid subscriptionId;
        using (var created = await partner.SendAsync(
                   HttpMethod.Post, "/partner/v1/webhooks", access, new { url = "https://hooks.example.az/q", eventTypes = new[] { "payout.created" } }, "pwh-1"))
        {
            var body = await api.JsonAsync(created);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            signingSecret = body.GetProperty("secret").GetString()!;
            subscriptionId = body.GetProperty("subscription").GetProperty("id").GetGuid();
            Assert.StartsWith("whsec_", signingSecret, StringComparison.Ordinal);
        }

        using (var list = await partner.SendAsync(HttpMethod.Get, "/partner/v1/webhooks", access))
        {
            var items = await api.JsonAsync(list);
            Assert.Equal(1, items.GetArrayLength());
            Assert.False(items[0].TryGetProperty("secret", out _));
        }

        // Hadisə → bir Pending çatdırılma; eyni hadisənin təkrarı ikinci sətir yaratmır.
        var payout = new PayoutCreated(StronglyTypedId.New<PayoutId>(), businessId, 120_00, api.Clock.UtcNow);
        Assert.True(await WebhookAsync(api, Message(payout)));
        Assert.False(await WebhookAsync(api, Message(payout)));
        Assert.Equal(1, await RowsAsync(api, subscriptionId));

        // Birinci cəhd 500 → Retrying, 1 dəqiqə sonra.
        var receiver = new FakeWebhookSender(HttpStatusCode.InternalServerError);
        Assert.Equal(1, await DispatchAsync(partner, api, receiver));
        var request = Assert.Single(receiver.Requests);
        Assert.Equal(("payout.created", WebhookDelivery.Signature(signingSecret, request.Timestamp, request.Body)), (request.EventType, request.Signature));
        Assert.Equal("payout.created", JsonDocument.Parse(request.Body).RootElement.GetProperty("type").GetString());
        Assert.Equal(0, await DispatchAsync(partner, api, receiver));

        api.Clock.UtcNow = api.Clock.UtcNow.AddMinutes(1);
        receiver.Status = HttpStatusCode.OK;
        Assert.Equal(1, await DispatchAsync(partner, api, receiver));
        Assert.Equal(request.DeliveryId, receiver.Requests[1].DeliveryId);

        var delivery = await LatestDeliveryAsync(api, partner, access);
        Assert.Equal(("Succeeded", 2), (delivery.GetProperty("status").GetString(), delivery.GetProperty("attempt").GetInt32()));

        // Əl ilə yenidən göndərmə → dərhal yeni cəhd.
        using (var redeliver = await partner.SendAsync(
                   HttpMethod.Post, $"/partner/v1/webhooks/deliveries/{request.DeliveryId}/redeliver", access, null, "pwh-2"))
        {
            Assert.Equal("Pending", (await api.JsonAsync(redeliver)).GetProperty("status").GetString());
        }

        Assert.Equal(1, await DispatchAsync(partner, api, receiver));
        Assert.Equal(3, receiver.Requests.Count);

        // Silinmiş abunəyə yeni çatdırılma yaranmır.
        using (var deleted = await partner.SendAsync(HttpMethod.Delete, $"/partner/v1/webhooks/{subscriptionId}", access))
        {
            Assert.True(deleted.IsSuccessStatusCode);
        }

        var before = await RowsAsync(api, subscriptionId);
        Assert.True(await WebhookAsync(api, Message(new PayoutCreated(StronglyTypedId.New<PayoutId>(), businessId, 5_00, api.Clock.UtcNow))));
        Assert.Equal(before, await RowsAsync(api, subscriptionId));
    }

    [Fact]
    public async Task Webhook_endpoints_require_the_webhooks_scope()
    {
        fixture.SkipIfUnavailable();
        await using var api = await fixture.HostAsync();
        await using var partner = new PartnerTestHost(api);
        var (_, client, secret) = await PartnerTestData.ClientAsync(api, "pwh-scope", [PartnerScopes.CodesRead]);
        var access = await partner.AccessTokenAsync(client.GetProperty("clientId").GetString()!, secret);

        using var response = await partner.SendAsync(HttpMethod.Get, "/partner/v1/webhooks", access);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<bool> WebhookAsync(ApiTestHost host, IntegrationMessage message)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
        var consumer = new WebhookConsumer(host.Services.GetRequiredService<IServiceScopeFactory>(), Options.Create(new WebhookOptions()));
        return await new InboxProcessor(context).ProcessAsync(consumer, message, host.Clock.UtcNow, Ct);
    }

    private static async Task<int> DispatchAsync(PartnerTestHost partner, ApiTestHost api, IWebhookSender sender)
    {
        await using var scope = partner.Services.CreateAsyncScope();
        var job = new DispatchWebhooksJob(
            scope.ServiceProvider.GetRequiredService<IWebhookRepository>(), scope.ServiceProvider.GetRequiredService<ISecretSealer>(), sender, api.Clock);
        return await job.RunAsync(Ct);
    }

    private static async Task<int> RowsAsync(ApiTestHost host, Guid subscriptionId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<QonaqetDbContext>();
        return await context.Set<WebhookDelivery>().Where(d => d.SubscriptionId == subscriptionId).Select(d => d.DeliveryId).Distinct().CountAsync(Ct);
    }

    private static async Task<JsonElement> LatestDeliveryAsync(ApiTestHost api, PartnerTestHost partner, string access)
    {
        using var response = await partner.SendAsync(HttpMethod.Get, "/partner/v1/webhooks/deliveries", access);
        var body = await api.JsonAsync(response);
        Assert.True(response.IsSuccessStatusCode, body.GetRawText());
        return Assert.Single(body.GetProperty("items").EnumerateArray());
    }

    private sealed class FakeWebhookSender(HttpStatusCode status) : IWebhookSender
    {
        public HttpStatusCode Status { get; set; } = status;

        public List<WebhookRequest> Requests { get; } = [];

        public Task<WebhookSendResult> SendAsync(WebhookRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var code = (int)Status;
            return Task.FromResult(new WebhookSendResult(code is >= 200 and < 300, code, "ok"));
        }
    }
}
