using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Qonaqet.Application.Features.Donations.Commands.DonateRemote;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Catalog;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Social;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>Onlayn ianələr ("İsmarla"): məbləğ serverdə hesablanır, ödəniş başladılır (<c>Idempotency-Key</c> məcburidir).</summary>
[ApiController]
[Route("api/v1/donations")]
[EnableRateLimiting(RateLimitPolicies.Payment)]
public sealed class DonationsController(ISender sender) : ControllerBase
{
    [HttpPost("remote")]
    public async Task<IActionResult> Remote(RemoteDonationRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new DonateRemoteCommand(
                BusinessId.From(request.BusinessId), CatalogItemId.From(request.CatalogItemId), CategoryId.From(request.CategoryId),
                request.Quantity, request.DonorDisplay, request.DonorName),
            cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult();
    }

    public sealed record RemoteDonationRequest(Guid BusinessId, Guid CatalogItemId, Guid CategoryId, int Quantity, DonorDisplay DonorDisplay, string? DonorName);
}
