using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Features.Social.Queries.ListOffers;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Categories;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>Sosial qonaqlıq ("İsmarla"): təkliflər.</summary>
[ApiController]
[Route("api/v1")]
public sealed class SocialController(ISender sender) : ControllerBase
{
    [HttpGet("offers")]
    public async Task<IActionResult> ListOffers([FromQuery] Guid? categoryId, [FromQuery] Guid? businessId, CancellationToken cancellationToken) =>
        (await sender.Send(
            new ListOffersQuery(categoryId is { } c ? CategoryId.From(c) : null, businessId is { } b ? BusinessId.From(b) : null),
            cancellationToken)).ToActionResult();
}
