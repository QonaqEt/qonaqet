using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Qonaqet.Application.Features.Social.Commands.CancelClaim;
using Qonaqet.Application.Features.Social.Commands.ClaimFromPool;
using Qonaqet.Application.Features.Social.Commands.ClaimOffer;
using Qonaqet.Application.Features.Social.Queries.GetMyClaims;
using Qonaqet.Application.Features.Social.Queries.GetMyDonations;
using Qonaqet.Application.Features.Social.Queries.ListOffers;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Social;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>Sosial qonaqlıq ("İsmarla"): təkliflər və claim-lər.</summary>
[ApiController]
[Route("api/v1")]
public sealed class SocialController(ISender sender) : ControllerBase
{
    [HttpGet("offers")]
    public async Task<IActionResult> ListOffers([FromQuery] Guid? categoryId, [FromQuery] Guid? businessId, CancellationToken cancellationToken) =>
        (await sender.Send(
            new ListOffersQuery(categoryId is { } c ? CategoryId.From(c) : null, businessId is { } b ? BusinessId.From(b) : null),
            cancellationToken)).ToActionResult();

    [HttpPost("offers/{offerId:guid}/claim")]
    [EnableRateLimiting(RateLimitPolicies.Claim)]
    public async Task<IActionResult> ClaimOffer(Guid offerId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ClaimOfferCommand(OfferId.From(offerId)), cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult();
    }

    /// <summary>"Hovuzdan al": acceptsPool biznesin kataloq məhsulu hovuz hesabına rezerv edilir.</summary>
    [HttpPost("pool-claims")]
    [EnableRateLimiting(RateLimitPolicies.Claim)]
    public async Task<IActionResult> ClaimFromPool(PoolClaimRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ClaimFromPoolCommand(BusinessId.From(request.BusinessId), Domain.Catalog.CatalogItemId.From(request.CatalogItemId)), cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult();
    }

    public sealed record PoolClaimRequest(Guid BusinessId, Guid CatalogItemId);

    [HttpPost("claims/{claimId:guid}/cancel")]
    public async Task<IActionResult> CancelClaim(Guid claimId, CancellationToken cancellationToken) =>
        (await sender.Send(new CancelClaimCommand(ClaimId.From(claimId)), cancellationToken)).ToActionResult();

    [HttpGet("me/claims")]
    [EnableRateLimiting(RateLimitPolicies.Claim)]
    public async Task<IActionResult> MyClaims(CancellationToken cancellationToken) =>
        (await sender.Send(new GetMyClaimsQuery(), cancellationToken)).ToActionResult();

    [HttpGet("me/donations")]
    public async Task<IActionResult> MyDonations(CancellationToken cancellationToken) =>
        (await sender.Send(new GetMyDonationsQuery(), cancellationToken)).ToActionResult();

    [HttpGet("me/claims/{claimId:guid}")]
    [EnableRateLimiting(RateLimitPolicies.Claim)]
    public async Task<IActionResult> MyClaim(Guid claimId, CancellationToken cancellationToken) =>
        (await sender.Send(new GetMyClaimQuery(ClaimId.From(claimId)), cancellationToken)).ToActionResult();
}
