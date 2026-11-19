using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Features.Users.Commands.DeleteMe;
using Qonaqet.Application.Features.Users.Commands.UpdateProfile;
using Qonaqet.Application.Features.Users.Queries.GetMe;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>Cari istifadəçinin hesabı. Autentifikasiya tələbi pipeline-dadır (atributsuz sorğular default olaraq tələb edir).</summary>
[ApiController]
[Route("api/v1/me")]
public sealed class MeController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        (await sender.Send(new GetMeQuery(), cancellationToken)).ToActionResult();

    [HttpPatch]
    public async Task<IActionResult> Update(UpdateProfileCommand command, CancellationToken cancellationToken) =>
        (await sender.Send(command, cancellationToken)).ToActionResult();

    [HttpDelete]
    public async Task<IActionResult> Delete([FromQuery] string? reason, CancellationToken cancellationToken) =>
        (await sender.Send(new DeleteMeCommand(reason), cancellationToken)).ToActionResult();
}
