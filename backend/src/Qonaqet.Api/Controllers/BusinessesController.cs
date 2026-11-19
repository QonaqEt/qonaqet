using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Features.Businesses.Commands.ApplyBusiness;
using Qonaqet.Application.Features.Businesses.Queries.GetPublicBusiness;
using Qonaqet.Application.Features.Businesses.Queries.SearchBusinesses;
using Qonaqet.Domain.Businesses;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>Bizneslər: ictimai axtarış və səhifə (anonim), müraciət.</summary>
[ApiController]
[Route("api/v1/businesses")]
public sealed class BusinessesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] double? lat,
        [FromQuery] double? lng,
        [FromQuery] double? radiusKm,
        [FromQuery] BusinessType? type,
        [FromQuery] bool? acceptsPool,
        [FromQuery] bool? acceptsNetworkVouchers,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken) =>
        (await sender.Send(new SearchBusinessesQuery(lat, lng, radiusKm, type, acceptsPool, acceptsNetworkVouchers, page, pageSize), cancellationToken)).ToActionResult();

    [HttpGet("{idOrSlug}")]
    public async Task<IActionResult> Get(string idOrSlug, CancellationToken cancellationToken) =>
        (await sender.Send(new GetPublicBusinessQuery(idOrSlug), cancellationToken)).ToActionResult();

    [HttpPost("apply")]
    public async Task<IActionResult> Apply(ApplyBusinessCommand request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(request, cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult();
    }
}
