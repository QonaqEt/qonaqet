using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Qonaqet.Application.Features.Social.Commands.RedeemCode;
using Qonaqet.Application.Features.Social.Queries.PreviewCode;
using Qonaqet.Domain.Businesses;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>
/// Biznes paneli: kod yoxlama və təhvil ("Kod yoxla / Təhvil ver"). Kod həmişə body-də gəlir (URL/log-a düşmür); endpoint-lər
/// rate-limitlidir. İşçi yalnız məhsulu, statusu və məbləği görür.
/// </summary>
[ApiController]
[Route("api/v1/business/{businessId:guid}/redeem")]
[EnableRateLimiting(RateLimitPolicies.Redeem)]
public sealed class BusinessRedeemController(ISender sender) : ControllerBase
{
    [HttpPost("preview")]
    public async Task<IActionResult> Preview(Guid businessId, CodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (await sender.Send(new PreviewCodeQuery(BusinessId.From(businessId), request.Code), cancellationToken)).ToActionResult();
    }

    [HttpPost]
    public async Task<IActionResult> Redeem(Guid businessId, CodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (await sender.Send(new RedeemCodeCommand(BusinessId.From(businessId), request.Code), cancellationToken)).ToActionResult();
    }

    public sealed record CodeRequest(string Code);
}
