using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Features.Businesses.Queries.ListMyBusinesses;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>Aktorun bizneslərinin siyahısı (şəbəkə üçün biznes dəyişdiricisi).</summary>
[ApiController]
[Route("api/v1/business/mine")]
public sealed class MyBusinessesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        (await sender.Send(new ListMyBusinessesQuery(), cancellationToken)).ToActionResult();
}
