using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Features.Admin.Commands.ActivateBusiness;
using Qonaqet.Application.Features.Admin.Commands.SuspendBusiness;
using Qonaqet.Application.Features.Admin.Queries.GetBusinessForAdmin;
using Qonaqet.Application.Features.Admin.Queries.ListBusinesses;
using Qonaqet.Domain.Businesses;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>Admin: biznes müraciətləri və statusları. Rol tələbi əmrlərin özündədir.</summary>
[ApiController]
[Route("api/v1/admin/businesses")]
public sealed class AdminBusinessesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] BusinessStatus? status, [FromQuery] bool includeDeleted, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        (await sender.Send(new ListBusinessesQuery(status, includeDeleted, page, pageSize), cancellationToken)).ToActionResult();

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        (await sender.Send(new GetBusinessForAdminQuery(BusinessId.From(id)), cancellationToken)).ToActionResult();

    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken) =>
        (await sender.Send(new ActivateBusinessCommand(BusinessId.From(id)), cancellationToken)).ToActionResult();

    [HttpPost("{id:guid}/suspend")]
    public async Task<IActionResult> Suspend(Guid id, SuspendRequest request, CancellationToken cancellationToken) =>
        (await sender.Send(new SuspendBusinessCommand(BusinessId.From(id), request.Reason), cancellationToken)).ToActionResult();

    public sealed record SuspendRequest(string Reason);
}
