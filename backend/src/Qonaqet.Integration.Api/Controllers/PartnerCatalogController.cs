using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Qonaqet.Application.Features.Integration.Commands.PartnerSyncCatalog;
using Qonaqet.Application.Features.Integration.Queries.GetPartnerCatalog;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Integration.Api.Controllers;

/// <summary>Kataloq sinxronu: POS menyusu <c>externalId</c> üzrə upsert, istəyə görə çatışmayanların soft delete-i.</summary>
[ApiController]
[Route(PartnerRoutes.Prefix + "/catalog")]
[EnableRateLimiting(RateLimitPolicies.Partner)]
public sealed class PartnerCatalogController(ISender sender) : ControllerBase
{
    [HttpGet("{businessId:guid}")]
    public async Task<IActionResult> Get(Guid businessId, CancellationToken cancellationToken) =>
        (await sender.Send(new GetPartnerCatalogQuery(businessId), cancellationToken)).ToActionResult();

    /// <summary>Toplu upsert (≤ 1000). <c>?prune=true</c>: sorğuda olmayan sinxron məhsulları soft delete edir.</summary>
    [HttpPut("{businessId:guid}")]
    public async Task<IActionResult> Sync(
        Guid businessId, SyncRequest request, [FromQuery] bool prune, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (await sender.Send(new PartnerSyncCatalogCommand(businessId, request.Items ?? [], prune), cancellationToken)).ToActionResult();
    }

    public sealed record SyncRequest(IReadOnlyList<PartnerCatalogItem>? Items);
}
