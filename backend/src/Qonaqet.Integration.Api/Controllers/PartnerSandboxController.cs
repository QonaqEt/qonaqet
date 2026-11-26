using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Qonaqet.Application.Features.Integration.Commands.PartnerSandbox;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Integration.Api.Controllers;

/// <summary>Sandbox alətləri: inteqrasiyanı sınamaq üçün test kodları (yalnız sandbox client-lər).</summary>
[ApiController]
[Route(PartnerRoutes.Prefix + "/sandbox")]
[EnableRateLimiting(RateLimitPolicies.Partner)]
public sealed class PartnerSandboxController(ISender sender) : ControllerBase
{
    /// <summary>Sandbox biznesdə təhvilə hazır test vauçeri; cavabda qısa kod və QR mətni. <c>Idempotency-Key</c> məcburidir.</summary>
    [HttpPost("test-codes")]
    public async Task<IActionResult> CreateTestCode(TestCodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(new CreateSandboxTestCodeCommand(request.BusinessId, request.AmountQepik), cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult();
    }

    public sealed record TestCodeRequest(Guid BusinessId, long? AmountQepik);
}
