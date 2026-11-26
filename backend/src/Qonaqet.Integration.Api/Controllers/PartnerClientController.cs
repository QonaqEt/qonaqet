using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Qonaqet.Application.Features.Integration.Queries.GetPartnerClient;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Integration.Api.Controllers;

/// <summary><c>GET /partner/v1/me</c>: token-in client-i (scope-lar, bizneslər, sandbox, limit).</summary>
[ApiController]
[Route(PartnerRoutes.Prefix + "/me")]
[EnableRateLimiting(RateLimitPolicies.Partner)]
public sealed class PartnerClientController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        (await sender.Send(new GetPartnerClientQuery(), cancellationToken)).ToActionResult();
}
