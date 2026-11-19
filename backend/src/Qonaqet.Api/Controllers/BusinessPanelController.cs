using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Features.Businesses.Commands.AddStaff;
using Qonaqet.Application.Features.Businesses.Commands.RemoveStaff;
using Qonaqet.Application.Features.Businesses.Queries.GetMyBusiness;
using Qonaqet.Application.Features.Businesses.Queries.ListStaff;
using Qonaqet.Application.Features.Catalog.Commands.CreateCatalogItem;
using Qonaqet.Application.Features.Catalog.Commands.DeleteCatalogItem;
using Qonaqet.Application.Features.Catalog.Commands.UpdateCatalogItem;
using Qonaqet.Application.Features.Catalog.Queries.ListCatalogItems;
using Qonaqet.Domain.Catalog;
using Qonaqet.Domain.Businesses;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>
/// Biznes paneli. Biznes həmişə URL-də açıq verilir (<c>/business/{businessId}/…</c>); giriş (sahib/işçi/admin) handler-də
/// üzvlüklə yoxlanılır, başqa biznesə müraciət <c>403</c> alır.
/// </summary>
[ApiController]
[Route("api/v1/business/{businessId:guid}")]
public sealed class BusinessPanelController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid businessId, CancellationToken cancellationToken) =>
        (await sender.Send(new GetMyBusinessQuery(BusinessId.From(businessId)), cancellationToken)).ToActionResult();

    [HttpGet("staff")]
    public async Task<IActionResult> ListStaff(Guid businessId, CancellationToken cancellationToken) =>
        (await sender.Send(new ListStaffQuery(BusinessId.From(businessId)), cancellationToken)).ToActionResult();

    [HttpPost("staff")]
    public async Task<IActionResult> AddStaff(Guid businessId, AddStaffRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(new AddStaffCommand(BusinessId.From(businessId), request.Email), cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult();
    }

    [HttpDelete("staff/{userId:guid}")]
    public async Task<IActionResult> RemoveStaff(Guid businessId, Guid userId, [FromQuery] string? reason, CancellationToken cancellationToken) =>
        (await sender.Send(new RemoveStaffCommand(BusinessId.From(businessId), userId, reason), cancellationToken)).ToActionResult();

    [HttpGet("catalog")]
    public async Task<IActionResult> ListCatalog(Guid businessId, CancellationToken cancellationToken) =>
        (await sender.Send(new ListCatalogItemsQuery(BusinessId.From(businessId)), cancellationToken)).ToActionResult();

    [HttpPost("catalog")]
    public async Task<IActionResult> CreateCatalogItem(Guid businessId, CatalogItemRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(
            new CreateCatalogItemCommand(
                BusinessId.From(businessId), request.Title, request.Description, request.PriceQepik, request.ImageFileId, request.Group, request.IsActive, request.IsGiftable),
            cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult();
    }

    [HttpPut("catalog/{itemId:guid}")]
    public async Task<IActionResult> UpdateCatalogItem(Guid businessId, Guid itemId, CatalogItemRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (await sender.Send(
            new UpdateCatalogItemCommand(
                BusinessId.From(businessId), CatalogItemId.From(itemId), request.Title, request.Description, request.PriceQepik, request.ImageFileId, request.Group,
                request.IsActive, request.IsGiftable),
            cancellationToken)).ToActionResult();
    }

    [HttpDelete("catalog/{itemId:guid}")]
    public async Task<IActionResult> DeleteCatalogItem(Guid businessId, Guid itemId, [FromQuery] string? reason, CancellationToken cancellationToken) =>
        (await sender.Send(new DeleteCatalogItemCommand(BusinessId.From(businessId), CatalogItemId.From(itemId), reason), cancellationToken)).ToActionResult();

    public sealed record AddStaffRequest(string Email);

    public sealed record CatalogItemRequest(
        string Title, string? Description, long PriceQepik, Guid? ImageFileId, string? Group, bool IsActive = true, bool IsGiftable = true);
}
