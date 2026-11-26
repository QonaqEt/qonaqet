using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Integration.Common;
using Qonaqet.Domain.Integration;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Integration.Commands.PartnerWebhooks;

/// <summary>
/// <c>POST /partner/v1/webhooks</c> (<c>webhooks</c>): abunə — URL (HTTPS; sandbox client-də HTTP də) və hadisə növləri. İmza secret-i
/// (<c>whsec_…</c>) cavabda bir dəfə göstərilir; serverdə şifrəli saxlanılır (ADR 0008). Client başına ən çox 10 abunə.
/// </summary>
[RequiresRole(PartnerScopes.Webhooks)]
public sealed record CreateWebhookSubscriptionCommand(string Url, IReadOnlyList<string> EventTypes) : ICommand<WebhookSubscriptionCreatedDto>, IPostgresCommand;

/// <summary><c>DELETE /partner/v1/webhooks/{id}</c>: abunə soft delete olunur; yeni çatdırılma yaranmır.</summary>
[RequiresRole(PartnerScopes.Webhooks)]
public sealed record DeleteWebhookSubscriptionCommand(WebhookSubscriptionId SubscriptionId) : ICommand, IPostgresCommand;

/// <summary><c>POST /partner/v1/webhooks/deliveries/{deliveryId}/redeliver</c>: çatdırılmanı dərhal yenidən növbəyə qoyur.</summary>
[RequiresRole(PartnerScopes.Webhooks)]
public sealed record RedeliverWebhookCommand(Guid DeliveryId) : ICommand<WebhookDeliveryDto>, INonTransactionalCommand;

/// <summary><c>GET /partner/v1/webhooks</c>.</summary>
[RequiresRole(PartnerScopes.Webhooks)]
public sealed record ListWebhookSubscriptionsQuery : IQuery<IReadOnlyList<WebhookSubscriptionDto>>;

/// <summary><c>GET /partner/v1/webhooks/deliveries?page=&amp;pageSize=</c>: çatdırılmaların cari vəziyyəti (jurnal).</summary>
[RequiresRole(PartnerScopes.Webhooks)]
public sealed record ListWebhookDeliveriesQuery(PageRequest Page) : IQuery<PagedResult<WebhookDeliveryDto>>;

public sealed record WebhookSubscriptionDto(Guid Id, string Url, IReadOnlyList<string> EventTypes, bool IsActive, DateTimeOffset CreatedAt);

/// <param name="Secret">Yalnız bu cavabda; qəbul edən imzanı bununla yoxlayır.</param>
public sealed record WebhookSubscriptionCreatedDto(WebhookSubscriptionDto Subscription, string Secret);

public sealed record WebhookDeliveryDto(
    Guid DeliveryId, Guid SubscriptionId, Guid EventId, string EventType, WebhookDeliveryStatus Status, int Attempt, DateTimeOffset NextAttemptAt,
    int? ResponseCode, string? ResponseBodySnippet, DateTimeOffset UpdatedAt);

public static class WebhookMapping
{
    public const string SecretPurpose = "webhook-secret";

    public static WebhookSubscriptionDto ToDto(this WebhookSubscription s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new WebhookSubscriptionDto(s.Id.Value, s.Url, s.EventTypes, s.IsActive, s.CreatedAt);
    }

    public static WebhookDeliveryDto ToDto(this WebhookDelivery d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return new WebhookDeliveryDto(
            d.DeliveryId, d.SubscriptionId, d.EventId, d.EventType, d.Status, d.Attempt, d.NextAttemptAt, d.ResponseCode, d.ResponseBodySnippet, d.CreatedAt);
    }
}

public sealed class CreateWebhookSubscriptionValidator : Validator<CreateWebhookSubscriptionCommand>
{
    public CreateWebhookSubscriptionValidator()
    {
        Rule("url", x => Uri.TryCreate(x.Url, UriKind.Absolute, out _), IntegrationErrors.WebhookUrlInvalid.Code, IntegrationErrors.WebhookUrlInvalid.Message);
        Rule(
            "eventTypes",
            x => x.EventTypes is { Count: > 0 } && x.EventTypes.All(WebhookEventTypes.IsKnown),
            "webhook_event_types_invalid",
            "Ən azı bir məlum hadisə növü seçin.");
    }
}

public sealed class CreateWebhookSubscriptionHandler(
    PartnerAccess access, IWebhookRepository webhooks, ISecretSealer sealer, ISecureRandom random, IClock clock)
    : ICommandHandler<CreateWebhookSubscriptionCommand, WebhookSubscriptionCreatedDto>
{
    public async Task<Result<WebhookSubscriptionCreatedDto>> Handle(CreateWebhookSubscriptionCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var client = await access.RequireAsync(cancellationToken).ConfigureAwait(false);
        if (client.IsFailure)
        {
            return client.Error;
        }

        var url = new Uri(request.Url, UriKind.Absolute);
        if (!WebhookSubscription.IsValidUrl(url, allowHttp: client.Value.IsSandbox))
        {
            return IntegrationErrors.WebhookUrlInvalid;
        }

        var existing = await webhooks.ListSubscriptionsAsync(client.Value.Id.Value, cancellationToken).ConfigureAwait(false);
        if (existing.Count >= WebhookSubscription.MaxPerClient)
        {
            return IntegrationErrors.WebhookLimitReached;
        }

        var secret = "whsec_" + random.Token(32);
        var subscription = WebhookSubscription.Create(client.Value.Id.Value, url, request.EventTypes, sealer.Seal(WebhookMapping.SecretPurpose, secret), clock.UtcNow);
        webhooks.AddSubscription(subscription);
        await webhooks.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new WebhookSubscriptionCreatedDto(subscription.ToDto(), secret);
    }
}

public sealed class DeleteWebhookSubscriptionHandler(PartnerAccess access, IWebhookRepository webhooks, IClock clock)
    : ICommandHandler<DeleteWebhookSubscriptionCommand>
{
    public async Task<Result> Handle(DeleteWebhookSubscriptionCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var client = await access.RequireAsync(cancellationToken).ConfigureAwait(false);
        if (client.IsFailure)
        {
            return client.Error;
        }

        var subscription = await webhooks.GetSubscriptionAsync(request.SubscriptionId, cancellationToken).ConfigureAwait(false);
        if (subscription is null || subscription.ClientId != client.Value.Id.Value)
        {
            return IntegrationErrors.WebhookNotFound;
        }

        subscription.SoftDelete(clock.UtcNow, client.Value.Id.Value, "partner_deleted");
        await webhooks.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

public sealed class RedeliverWebhookHandler(PartnerAccess access, IWebhookRepository webhooks, IClock clock)
    : ICommandHandler<RedeliverWebhookCommand, WebhookDeliveryDto>
{
    public async Task<Result<WebhookDeliveryDto>> Handle(RedeliverWebhookCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var client = await access.RequireAsync(cancellationToken).ConfigureAwait(false);
        if (client.IsFailure)
        {
            return client.Error;
        }

        var latest = await webhooks.LatestAsync(request.DeliveryId, cancellationToken).ConfigureAwait(false);
        if (latest is null || latest.ClientId != client.Value.Id.Value)
        {
            return IntegrationErrors.WebhookDeliveryNotFound;
        }

        if (latest.Status is WebhookDeliveryStatus.Sending or WebhookDeliveryStatus.Pending)
        {
            return latest.ToDto(); // artıq növbədədir
        }

        var queued = latest.Redeliver(clock.UtcNow);
        return await webhooks.TryAppendAsync(queued, cancellationToken).ConfigureAwait(false) ? queued.ToDto() : IntegrationErrors.WebhookDeliveryBusy;
    }
}

public sealed class ListWebhookSubscriptionsHandler(PartnerAccess access, IWebhookRepository webhooks)
    : IQueryHandler<ListWebhookSubscriptionsQuery, IReadOnlyList<WebhookSubscriptionDto>>
{
    public async Task<Result<IReadOnlyList<WebhookSubscriptionDto>>> Handle(ListWebhookSubscriptionsQuery request, CancellationToken cancellationToken)
    {
        var client = await access.RequireAsync(cancellationToken).ConfigureAwait(false);
        if (client.IsFailure)
        {
            return client.Error;
        }

        return (await webhooks.ListSubscriptionsAsync(client.Value.Id.Value, cancellationToken).ConfigureAwait(false)).Select(s => s.ToDto()).ToList();
    }
}

public sealed class ListWebhookDeliveriesHandler(PartnerAccess access, IWebhookRepository webhooks)
    : IQueryHandler<ListWebhookDeliveriesQuery, PagedResult<WebhookDeliveryDto>>
{
    public async Task<Result<PagedResult<WebhookDeliveryDto>>> Handle(ListWebhookDeliveriesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var client = await access.RequireAsync(cancellationToken).ConfigureAwait(false);
        if (client.IsFailure)
        {
            return client.Error;
        }

        var page = await webhooks.ListLatestForClientAsync(client.Value.Id.Value, request.Page, cancellationToken).ConfigureAwait(false);
        return new PagedResult<WebhookDeliveryDto>([.. page.Items.Select(d => d.ToDto())], page.Total);
    }
}
