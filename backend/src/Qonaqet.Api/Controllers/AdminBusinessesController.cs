using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Features.Admin.Commands.ActivateBusiness;
using Qonaqet.Application.Features.Admin.Commands.DeleteBusiness;
using Qonaqet.Application.Features.Admin.Commands.RestoreBusiness;
using Qonaqet.Application.Features.Admin.Commands.SetBusinessSandbox;
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

    /// <summary>Soft delete (kataloq və üzvlüklər kaskadla); fiziki silmə yoxdur.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] string reason, CancellationToken cancellationToken) =>
        (await sender.Send(new DeleteBusinessCommand(BusinessId.From(id), reason), cancellationToken)).ToActionResult();

    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken) =>
        (await sender.Send(new RestoreBusinessCommand(BusinessId.From(id)), cancellationToken)).ToActionResult();

    /// <summary>Sandbox (test) biznesi: yalnız sandbox inteqrasiya client-ləri ilə işləyir, axtarışda görünmür, ledger-ə yazılmır (E13-T09).</summary>
    [HttpPut("{id:guid}/sandbox")]
    public async Task<IActionResult> SetSandbox(Guid id, SandboxRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (await sender.Send(new SetBusinessSandboxCommand(BusinessId.From(id), request.IsSandbox), cancellationToken)).ToActionResult();
    }

    public sealed record SuspendRequest(string Reason);

    public sealed record SandboxRequest(bool IsSandbox);
}
