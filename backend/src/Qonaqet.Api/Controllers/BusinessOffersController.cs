using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Features.Social.Commands.CreateDailyOffer;
using Qonaqet.Application.Features.Social.Commands.CreateSuspendedOffer;
using Qonaqet.Application.Features.Social.Commands.DeleteOffer;
using Qonaqet.Application.Features.Social.Commands.UpdateOffer;
using Qonaqet.Application.Features.Social.Queries.ListBusinessOffers;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Catalog;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Social;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>Biznes paneli: sosial qonaqlıq təklifləri (gündəlik və askıda). Giriş handler-də üzvlüklə yoxlanılır.</summary>
[ApiController]
[Route("api/v1/business/{businessId:guid}/offers")]
public sealed class BusinessOffersController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid businessId, CancellationToken cancellationToken) =>
        (await sender.Send(new ListBusinessOffersQuery(BusinessId.From(businessId)), cancellationToken)).ToActionResult();

    [HttpPost]
    public async Task<IActionResult> CreateDaily(Guid businessId, DailyOfferRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(
            new CreateDailyOfferCommand(
                BusinessId.From(businessId), CategoryId.From(request.CategoryId), request.Title, ItemId(request.CatalogItemId), request.DailyQuota,
                request.AvailableFrom, request.AvailableTo, request.DonorDisplay, request.DonorName, request.IsActive),
            cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult();
    }

    [HttpPost("suspended")]
    public async Task<IActionResult> CreateSuspended(Guid businessId, SuspendedOfferRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(
            new CreateSuspendedOfferCommand(
                BusinessId.From(businessId), CategoryId.From(request.CategoryId), request.Title, ItemId(request.CatalogItemId), request.Quantity,
                request.DonorDisplay, request.DonorName, request.DonorEmail),
            cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult();
    }

    [HttpPut("{offerId:guid}")]
    public async Task<IActionResult> Update(Guid businessId, Guid offerId, UpdateOfferRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (await sender.Send(
            new UpdateOfferCommand(
                BusinessId.From(businessId), OfferId.From(offerId), request.Title, ItemId(request.CatalogItemId), request.DailyQuota,
                request.AvailableFrom, request.AvailableTo, request.DonorDisplay, request.DonorName, request.IsActive),
            cancellationToken)).ToActionResult();
    }

    [HttpDelete("{offerId:guid}")]
    public async Task<IActionResult> Delete(Guid businessId, Guid offerId, [FromQuery] string? reason, CancellationToken cancellationToken) =>
        (await sender.Send(new DeleteOfferCommand(BusinessId.From(businessId), OfferId.From(offerId), reason), cancellationToken)).ToActionResult();

    private static CatalogItemId? ItemId(Guid? id) => id is { } value ? CatalogItemId.From(value) : null;

    public sealed record DailyOfferRequest(
        Guid CategoryId,
        string Title,
        Guid? CatalogItemId,
        int DailyQuota,
        string? AvailableFrom,
        string? AvailableTo,
        DonorDisplay DonorDisplay = DonorDisplay.Named,
        string? DonorName = null,
        bool IsActive = true);

    public sealed record SuspendedOfferRequest(
        Guid CategoryId,
        string Title,
        Guid? CatalogItemId,
        int Quantity,
        DonorDisplay DonorDisplay = DonorDisplay.Anonymous,
        string? DonorName = null,
        string? DonorEmail = null);

    public sealed record UpdateOfferRequest(
        string Title,
        Guid? CatalogItemId,
        int DailyQuota,
        string? AvailableFrom,
        string? AvailableTo,
        DonorDisplay DonorDisplay,
        string? DonorName,
        bool IsActive = true);
}
