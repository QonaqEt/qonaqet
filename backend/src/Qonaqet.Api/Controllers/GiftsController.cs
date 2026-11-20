using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Qonaqet.Application.Features.Gifts.Commands.CancelGift;
using Qonaqet.Application.Features.Gifts.Commands.CreateGift;
using Qonaqet.Application.Features.Gifts.Commands.ReissueGiftLink;
using Qonaqet.Application.Features.Gifts.Commands.RevealGiftLink;
using Qonaqet.Application.Features.Gifts.Queries.MyGifts;
using Qonaqet.Application.Features.Gifts.Common;
using Qonaqet.Domain.Gifts;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>Hədiyyələr ("Hədiyyə et"): yaradılma və ödəniş, link, göndərilən/alınan hədiyyələr.</summary>
[ApiController]
[Route("api/v1/gifts")]
public sealed class GiftsController(ISender sender) : ControllerBase
{
    /// <summary>Hədiyyə yaradır və ödənişi başladır (<c>Idempotency-Key</c> məcburidir). Məbləğ serverdə hesablanır.</summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Payment)]
    public async Task<IActionResult> Create(CreateGiftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(
            new CreateGiftCommand(
                new GiftSelection(request.Kind, request.Scope, request.BusinessId, request.Items, request.AmountQepik),
                request.RecipientName ?? string.Empty,
                request.RecipientEmail,
                request.Theme,
                request.Message,
                request.SenderDisplay,
                request.DeliverAt,
                request.ExpiryAction ?? GiftExpiryAction.RefundToPayer),
            cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult();
    }

    /// <summary>Paylaşma linki (bir dəfə). Token cavabdadır və heç yerdə loglanmır.</summary>
    [HttpPost("{giftId:guid}/share-link")]
    [EnableRateLimiting(RateLimitPolicies.GiftLink)]
    public async Task<IActionResult> ShareLink(Guid giftId, CancellationToken cancellationToken) =>
        (await sender.Send(new RevealGiftLinkCommand(GiftId.From(giftId)), cancellationToken)).ToActionResult();

    /// <summary>Yeni link (köhnəsi dərhal etibarsız olur); yalnız göndərən.</summary>
    [HttpPost("{giftId:guid}/reissue-link")]
    [EnableRateLimiting(RateLimitPolicies.GiftLink)]
    public async Task<IActionResult> ReissueLink(Guid giftId, CancellationToken cancellationToken) =>
        (await sender.Send(new ReissueGiftLinkCommand(GiftId.From(giftId)), cancellationToken)).ToActionResult();

    /// <summary>Hələ çatdırılmamış hədiyyənin ləğvi; ödəniş tam refund olunur (idempotent).</summary>
    [HttpPost("{giftId:guid}/cancel")]
    [EnableRateLimiting(RateLimitPolicies.Payment)]
    public async Task<IActionResult> Cancel(Guid giftId, CancellationToken cancellationToken) =>
        (await sender.Send(new CancelGiftCommand(GiftId.From(giftId)), cancellationToken)).ToActionResult();

    [HttpGet("sent")]
    public async Task<IActionResult> Sent(CancellationToken cancellationToken) =>
        (await sender.Send(new GetSentGiftsQuery(), cancellationToken)).ToActionResult();

    [HttpGet("received")]
    public async Task<IActionResult> Received(CancellationToken cancellationToken) =>
        (await sender.Send(new GetReceivedGiftsQuery(), cancellationToken)).ToActionResult();

    [HttpGet("{giftId:guid}")]
    public async Task<IActionResult> Get(Guid giftId, CancellationToken cancellationToken) =>
        (await sender.Send(new GetGiftQuery(GiftId.From(giftId)), cancellationToken)).ToActionResult();

    public sealed record CreateGiftRequest(
        GiftKind Kind,
        GiftScope Scope,
        Guid? BusinessId,
        IReadOnlyList<GiftItemRequest>? Items,
        long? AmountQepik,
        string? RecipientName,
        string? RecipientEmail,
        GiftTheme Theme,
        string? Message,
        SenderDisplay SenderDisplay,
        DateTimeOffset? DeliverAt,
        GiftExpiryAction? ExpiryAction);
}
