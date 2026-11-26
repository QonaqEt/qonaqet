using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Contracts.Webhooks;
using Qonaqet.Domain.Catalog;
using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Integration;
using Qonaqet.Domain.Ledger;
using Qonaqet.Domain.Social;
using Qonaqet.Infrastructure.Messaging.Inbox;
using Qonaqet.Infrastructure.Messaging.Outbox;
using Qonaqet.Infrastructure.Persistence.Postgres;

namespace Qonaqet.Infrastructure.Messaging.Consumers;

/// <summary>Webhook parametrləri (<c>Integration</c> bölməsi).</summary>
public sealed class WebhookOptions
{
    public const string SectionName = "Integration";

    /// <summary>Balans bu həddən aşağı düşəndə <c>partner.balance.low</c> (default 50 AZN).</summary>
    public long BalanceLowThresholdQepik { get; set; } = 50_00;
}

/// <summary>
/// Domen hadisələrini partnyor webhook-larına çevirir: hər uyğun aktiv abunə üçün jurnala <c>Pending</c> sətri (inbox ilə eyni Postgres
/// tranzaksiyasında; çatdırılma ID-si hadisə + abunə üzrə deterministikdir, təkrar emal ikinci sətir yaratmır). Göndərişi
/// <c>DispatchWebhooksJob</c> edir. Kimə: təhvil/payout/kataloq hadisələri — <c>business_ids</c>-ində həmin biznes olan client-lərə; hədiyyə
/// hadisələri — hədiyyəni buraxan issuer client-ə; balans — client-in özünə. Payload-lar <c>Qonaqet.Contracts.Webhooks</c> v1 DTO-larıdır.
/// </summary>
public sealed class WebhookConsumer(IServiceScopeFactory scopes, IOptions<WebhookOptions> options) : IIntegrationEventConsumer
{
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    private static readonly string GiftRedeemedType = typeof(GiftRedeemed).FullName!;
    private static readonly string ClaimRedeemedType = typeof(ClaimRedeemed).FullName!;
    private static readonly string GiftReversedType = typeof(GiftRedemptionReversed).FullName!;
    private static readonly string ClaimReversedType = typeof(ClaimRedemptionReversed).FullName!;
    private static readonly string PayoutCreatedType = typeof(PayoutCreated).FullName!;
    private static readonly string CatalogSyncedType = typeof(CatalogSynced).FullName!;
    private static readonly string GiftFundedType = typeof(PartnerGiftFunded).FullName!;
    private static readonly string GiftDeliveredType = typeof(GiftDelivered).FullName!;
    private static readonly string GiftExpiredType = typeof(GiftExpired).FullName!;

    public string Name => "webhooks";

    public IReadOnlyCollection<string> EventTypes { get; } =
        [GiftRedeemedType, ClaimRedeemedType, GiftReversedType, ClaimReversedType, PayoutCreatedType, CatalogSyncedType, GiftFundedType, GiftDeliveredType, GiftExpiredType];

    public async Task HandleAsync(IntegrationMessage message, QonaqetDbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);
        var at = Read<AtPayload>(message).At;

        if (message.Type == GiftRedeemedType)
        {
            var e = Read<GiftRedeemedPayload>(message);
            await PublishToBusinessAsync(context, message, e.BusinessId, WebhookEventTypes.RedemptionCreated,
                new RedemptionWebhookV1(e.RedemptionId, e.BusinessId, "Gift", e.ValueQepik), at, cancellationToken).ConfigureAwait(false);
            await PublishGiftAsync(context, message, e.GiftId, WebhookEventTypes.GiftRedeemed, e.ValueQepik, at, cancellationToken).ConfigureAwait(false);
        }
        else if (message.Type == ClaimRedeemedType)
        {
            var e = Read<ClaimPayload>(message);
            await PublishToBusinessAsync(context, message, e.BusinessId, WebhookEventTypes.RedemptionCreated,
                new RedemptionWebhookV1(e.ClaimId, e.BusinessId, "Claim", e.AmountQepik ?? 0), at, cancellationToken).ConfigureAwait(false);
        }
        else if (message.Type == GiftReversedType)
        {
            var e = Read<GiftReversedPayload>(message);
            await PublishToBusinessAsync(context, message, e.BusinessId, WebhookEventTypes.RedemptionReversed,
                new RedemptionWebhookV1(e.RedemptionId, e.BusinessId, "Gift", e.ValueQepik), at, cancellationToken).ConfigureAwait(false);
        }
        else if (message.Type == ClaimReversedType)
        {
            var e = Read<ClaimPayload>(message);
            await PublishToBusinessAsync(context, message, e.BusinessId, WebhookEventTypes.RedemptionReversed,
                new RedemptionWebhookV1(e.ClaimId, e.BusinessId, "Claim", e.AmountQepik ?? 0), at, cancellationToken).ConfigureAwait(false);
        }
        else if (message.Type == PayoutCreatedType)
        {
            var e = Read<PayoutPayload>(message);
            await PublishToBusinessAsync(context, message, e.BusinessId, WebhookEventTypes.PayoutCreated,
                new PayoutWebhookV1(e.PayoutId, e.BusinessId, e.Amount), at, cancellationToken).ConfigureAwait(false);
        }
        else if (message.Type == CatalogSyncedType)
        {
            var e = Read<CatalogPayload>(message);
            await PublishToBusinessAsync(context, message, e.BusinessId, WebhookEventTypes.CatalogSynced,
                new CatalogSyncedWebhookV1(e.BusinessId, e.Created, e.Updated, e.Unchanged, e.Pruned), at, cancellationToken).ConfigureAwait(false);
        }
        else if (message.Type == GiftFundedType)
        {
            var e = Read<FundedPayload>(message);
            await PublishGiftAsync(context, message, e.GiftId, WebhookEventTypes.GiftCreated, null, at, cancellationToken).ConfigureAwait(false);
            await PublishBalanceLowAsync(context, message, e.ClientId, e.AmountQepik, at, cancellationToken).ConfigureAwait(false);
        }
        else if (message.Type == GiftDeliveredType || message.Type == GiftExpiredType)
        {
            var e = Read<GiftIdPayload>(message);
            var type = message.Type == GiftDeliveredType ? WebhookEventTypes.GiftDelivered : WebhookEventTypes.GiftExpired;
            await PublishGiftAsync(context, message, e.GiftId, type, null, at, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task PublishToBusinessAsync<T>(
        QonaqetDbContext context, IntegrationMessage message, Guid businessId, string eventType, T data, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var clients = await context.Set<IntegrationClient>().AsNoTracking()
            .Where(c => EF.Property<List<Guid>>(c, "_businessIds").Contains(businessId))
            .Select(c => c.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        await PublishAsync(context, message, [.. clients.Select(c => c.Value)], eventType, data, at, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Hədiyyə hadisəsi yalnız issuer partnyorun hədiyyəsi üçündür (şəxsi/korporativ hədiyyələrdə heç nə).</summary>
    private async Task PublishGiftAsync(
        QonaqetDbContext context, IntegrationMessage message, Guid giftId, string eventType, long? redeemedNow, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var gift = await scope.ServiceProvider.GetRequiredService<IGiftRepository>().GetAsync(GiftId.From(giftId), cancellationToken).ConfigureAwait(false);
        if (gift?.PartnerClientId is not { } clientId)
        {
            return;
        }

        var data = new GiftWebhookV1(
            gift.Id.Value, gift.ExternalReference, gift.Status.ToString(), gift.AmountQepik, gift.RemainingQepik, redeemedNow, gift.ExpiresAt);
        await PublishAsync(context, message, [clientId], eventType, data, at, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Balans həddi bu silinmə ilə keçildisə (əvvəl ≥ hədd, indi &lt; hədd) — hər düşüşdə bir dəfə.</summary>
    private async Task PublishBalanceLowAsync(
        QonaqetDbContext context, IntegrationMessage message, Guid clientId, long debited, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var account = LedgerAccount.PartnerClient(clientId);
        var balance = await context.Set<LedgerEntry>().AsNoTracking()
            .Where(e => e.AccountType == account.Type && e.AccountId == account.Id)
            .SumAsync(e => e.Amount, cancellationToken).ConfigureAwait(false);
        var threshold = options.Value.BalanceLowThresholdQepik;
        if (balance < threshold && balance + debited >= threshold)
        {
            await PublishAsync(context, message, [clientId], WebhookEventTypes.PartnerBalanceLow, new PartnerBalanceLowWebhookV1(balance, threshold), at,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task PublishAsync<T>(
        QonaqetDbContext context, IntegrationMessage message, IReadOnlyCollection<Guid> clientIds, string eventType, T data, DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        if (clientIds.Count == 0)
        {
            return;
        }

        var subscriptions = (await context.Set<WebhookSubscription>().AsNoTracking()
                .Where(s => clientIds.Contains(s.ClientId) && s.IsActive).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Where(s => s.Wants(eventType)).ToList();
        if (subscriptions.Count == 0)
        {
            return;
        }

        var body = JsonSerializer.Serialize(
            new WebhookEnvelopeV1<T>(message.MessageId, eventType, WebhookEnvelopeV1<T>.Version, at, data), PayloadJson);
        foreach (var subscription in subscriptions)
        {
            var deliveryId = WebhookDelivery.DeliveryIdFor(message.MessageId, subscription.Id.Value, eventType);
            if (!await context.Set<WebhookDelivery>().AnyAsync(d => d.DeliveryId == deliveryId, cancellationToken).ConfigureAwait(false))
            {
                context.Set<WebhookDelivery>().Add(WebhookDelivery.Pending(subscription, message.MessageId, eventType, body, at));
            }
        }
    }

    private static T Read<T>(IntegrationMessage message) =>
        JsonSerializer.Deserialize<T>(message.Payload, OutboxSerializer.JsonOptions)
        ?? throw new InvalidOperationException($"Boş {message.Type} mesajı.");

    private sealed record AtPayload(DateTimeOffset At);

    private sealed record GiftRedeemedPayload(Guid GiftId, Guid RedemptionId, Guid BusinessId, long ValueQepik);

    private sealed record ClaimPayload(Guid ClaimId, Guid BusinessId, long? AmountQepik);

    private sealed record GiftReversedPayload(Guid GiftId, Guid RedemptionId, Guid BusinessId, long ValueQepik);

    private sealed record PayoutPayload(Guid PayoutId, Guid BusinessId, long Amount);

    private sealed record CatalogPayload(Guid BusinessId, int Created, int Updated, int Unchanged, int Pruned);

    private sealed record FundedPayload(Guid GiftId, Guid ClientId, long AmountQepik);

    private sealed record GiftIdPayload(Guid GiftId);
}
