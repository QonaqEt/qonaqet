using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Qonaqet.Application.Features.Campaigns.Commands;
using Qonaqet.Application.Features.Donations.Commands.DonatePool;
using Qonaqet.Application.Features.Donations.Commands.DonateRemote;
using Qonaqet.Application.Features.Donations.Queries.GetMyPoolDonations;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Catalog;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Social;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>Onlayn ianələr ("İsmarla"): məbləğ serverdə hesablanır, ödəniş başladılır (<c>Idempotency-Key</c> məcburidir).</summary>
[ApiController]
[Route("api/v1")]
[EnableRateLimiting(RateLimitPolicies.Payment)]
public sealed class DonationsController(ISender sender) : ControllerBase
{
    [HttpPost("donations/remote")]
    public async Task<IActionResult> Remote(RemoteDonationRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new DonateRemoteCommand(
                BusinessId.From(request.BusinessId), CatalogItemId.From(request.CatalogItemId), CategoryId.From(request.CategoryId),
                request.Quantity, request.DonorDisplay, request.DonorName),
            cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult();
    }

    [HttpPost("donations/pool")]
    public async Task<IActionResult> Pool(AmountDonationRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new DonatePoolCommand(CategoryId.From(request.CategoryId ?? Guid.Empty), request.AmountQepik, request.DonorDisplay, request.DonorName), cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult();
    }

    [HttpPost("donations/campaign")]
    public async Task<IActionResult> Campaign(AmountDonationRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new DonateCampaignCommand(CampaignId.From(request.CampaignId ?? Guid.Empty), request.AmountQepik, request.DonorDisplay, request.DonorName), cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult();
    }

    [HttpGet("me/donations/pools")]
    public async Task<IActionResult> MyPoolDonations(CancellationToken cancellationToken) =>
        (await sender.Send(new GetMyPoolDonationsQuery(), cancellationToken)).ToActionResult();

    public sealed record AmountDonationRequest(Guid? CategoryId, Guid? CampaignId, long AmountQepik, DonorDisplay DonorDisplay, string? DonorName);

    public sealed record RemoteDonationRequest(Guid BusinessId, Guid CatalogItemId, Guid CategoryId, int Quantity, DonorDisplay DonorDisplay, string? DonorName);
}
