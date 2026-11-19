using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Features.Businesses.Commands.ApplyBusiness;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>Bizneslər: müraciət (və E04-T05-dən ictimai axtarış).</summary>
[ApiController]
[Route("api/v1/businesses")]
public sealed class BusinessesController(ISender sender) : ControllerBase
{
    [HttpPost("apply")]
    public async Task<IActionResult> Apply(ApplyBusinessCommand request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(request, cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult();
    }
}
