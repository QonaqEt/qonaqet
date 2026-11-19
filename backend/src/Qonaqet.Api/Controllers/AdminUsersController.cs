using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Features.Admin.Commands.SetUserRoles;
using Qonaqet.Domain.Users;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>Admin: istifadəçilərin idarəsi. Rol tələbi əmrin özündədir (<c>[RequiresRole(Admin)]</c>), controller-də deyil.</summary>
[ApiController]
[Route("api/v1/admin/users")]
public sealed class AdminUsersController(ISender sender) : ControllerBase
{
    [HttpPut("{id:guid}/roles")]
    public async Task<IActionResult> SetRoles(Guid id, SetRolesRequest request, CancellationToken cancellationToken) =>
        (await sender.Send(new SetUserRolesCommand(UserId.From(id), request.Roles), cancellationToken)).ToActionResult();

    public sealed record SetRolesRequest(IReadOnlyList<Role> Roles);
}
