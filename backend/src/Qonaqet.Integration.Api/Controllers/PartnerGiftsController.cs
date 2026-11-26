using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Qonaqet.Application.Common;
using Qonaqet.Application.Features.Gifts.Common;
using Qonaqet.Application.Features.Integration.Commands.PartnerGifts;
using Qonaqet.Application.Features.Integration.Commands.PartnerTopUp;
using Qonaqet.Application.Features.Integration.Queries.PartnerGifts;
using Qonaqet.Domain.Gifts;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Integration.Api.Controllers;

/// <summary>Issuer partnyorlar: hədiyyə buraxılışı (ön ödənişli balansdan), ləğv, balans, hərəkətlər və kartla balans artımı.</summary>
[ApiController]
[Route(PartnerRoutes.Prefix)]
[EnableRateLimiting(RateLimitPolicies.Partner)]
public sealed class PartnerGiftsController(ISender sender) : ControllerBase
{
    /// <summary>Hədiyyə buraxır; link cavabda bir dəfə qaytarılır. <c>Idempotency-Key</c> məcburidir.</summary>
    [HttpPost("gifts")]
    public async Task<IActionResult> Issue(IssueRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(
            new PartnerIssueGiftCommand(
                request.What!, request.RecipientName ?? string.Empty, request.RecipientEmail, request.Theme, request.Message, request.DeliverAt,
                request.ExternalReference),
            cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult();
    }

    [HttpGet("gifts/{giftId:guid}")]
    public async Task<IActionResult> Get(Guid giftId, CancellationToken cancellationToken) =>
        (await sender.Send(new GetPartnerGiftQuery(GiftId.From(giftId)), cancellationToken)).ToActionResult();

    /// <summary>Hələ çatdırılmamış hədiyyəni ləğv edir; məbləğ balansa qayıdır.</summary>
    [HttpPost("gifts/{giftId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid giftId, CancellationToken cancellationToken) =>
        (await sender.Send(new PartnerCancelGiftCommand(GiftId.From(giftId)), cancellationToken)).ToActionResult();

    [HttpGet("balance")]
    public async Task<IActionResult> Balance(CancellationToken cancellationToken) =>
        (await sender.Send(new GetPartnerBalanceQuery(), cancellationToken)).ToActionResult();

    [HttpGet("ledger")]
    public async Task<IActionResult> Ledger([FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        (await sender.Send(new GetPartnerLedgerQuery(PageRequest.Of(page, pageSize)), cancellationToken)).ToActionResult();

    /// <summary>Kartla balans artımı: ödəniş səhifəsinin linki qaytarılır.</summary>
    [HttpPost("balance/top-ups")]
    public async Task<IActionResult> TopUp(TopUpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (await sender.Send(new StartPartnerTopUpCommand(request.AmountQepik), cancellationToken)).ToActionResult();
    }

    public sealed record IssueRequest(
        GiftSelection? What,
        string? RecipientName,
        string? RecipientEmail,
        GiftTheme Theme,
        string? Message,
        DateTimeOffset? DeliverAt,
        string? ExternalReference);

    public sealed record TopUpRequest(long AmountQepik);
}
