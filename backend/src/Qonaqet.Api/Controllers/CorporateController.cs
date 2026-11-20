using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Features.Corporate.Queries.CorporateOrders;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>
/// Korporativ portal (<c>CorporateUser</c>). Şirkət cari istifadəçinin hesabından oxunur; başqa şirkətin məlumatı görünmür.
/// </summary>
[ApiController]
[Route("api/v1/corporate")]
public sealed class CorporateController(ISender sender) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken) =>
        (await sender.Send(new GetMyCorporateQuery(), cancellationToken)).ToActionResult();
}
