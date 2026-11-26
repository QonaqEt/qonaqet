using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Qonaqet.Application.Common;
using Qonaqet.Application.Features.Integration.Commands.PartnerWebhooks;
using Qonaqet.Domain.Integration;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Integration.Api.Controllers;

/// <summary>Webhook abunələri, çatdırılma jurnalı və əl ilə yenidən göndərmə.</summary>
[ApiController]
[Route(PartnerRoutes.Prefix + "/webhooks")]
[EnableRateLimiting(RateLimitPolicies.Partner)]
public sealed class PartnerWebhooksController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        (await sender.Send(new ListWebhookSubscriptionsQuery(), cancellationToken)).ToActionResult();

    /// <summary>Abunə; imza secret-i cavabda bir dəfə. <c>Idempotency-Key</c> məcburidir.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(SubscriptionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(new CreateWebhookSubscriptionCommand(request.Url ?? string.Empty, request.EventTypes ?? []), cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult();
    }

    /// <summary>Soft delete.</summary>
    [HttpDelete("{subscriptionId:guid}")]
    public async Task<IActionResult> Delete(Guid subscriptionId, CancellationToken cancellationToken) =>
        (await sender.Send(new DeleteWebhookSubscriptionCommand(WebhookSubscriptionId.From(subscriptionId)), cancellationToken)).ToActionResult();

    [HttpGet("deliveries")]
    public async Task<IActionResult> Deliveries([FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        (await sender.Send(new ListWebhookDeliveriesQuery(PageRequest.Of(page, pageSize)), cancellationToken)).ToActionResult();

    [HttpPost("deliveries/{deliveryId:guid}/redeliver")]
    public async Task<IActionResult> Redeliver(Guid deliveryId, CancellationToken cancellationToken) =>
        (await sender.Send(new RedeliverWebhookCommand(deliveryId), cancellationToken)).ToActionResult();

    public sealed record SubscriptionRequest(string? Url, IReadOnlyList<string>? EventTypes);
}
