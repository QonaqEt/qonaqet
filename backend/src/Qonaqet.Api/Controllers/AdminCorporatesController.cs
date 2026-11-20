using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Features.Corporate.Commands.AdminCorporates;
using Qonaqet.Domain.Corporate;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>Admin: şirkətlər, şirkət istifadəçiləri.</summary>
[ApiController]
[Route("api/v1/admin")]
public sealed class AdminCorporatesController(ISender sender) : ControllerBase
{
    [HttpGet("corporates")]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        (await sender.Send(new ListCorporatesQuery(), cancellationToken)).ToActionResult();

    [HttpPost("corporates")]
    public async Task<IActionResult> Create(CorporateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(new CreateCorporateCommand(request.ToDetails()), cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult();
    }

    [HttpPut("corporates/{corporateId:guid}")]
    public async Task<IActionResult> Update(Guid corporateId, CorporateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (await sender.Send(new UpdateCorporateCommand(CorporateId.From(corporateId), request.ToDetails()), cancellationToken)).ToActionResult();
    }

    /// <summary>Mövcud istifadəçini şirkətə bağlayır (<c>CorporateUser</c> rolu).</summary>
    [HttpPost("corporates/{corporateId:guid}/users")]
    public async Task<IActionResult> AssignUser(Guid corporateId, AssignUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (await sender.Send(new AssignCorporateUserCommand(CorporateId.From(corporateId), request.Email ?? string.Empty), cancellationToken))
            .ToActionResult();
    }

    public sealed record CorporateRequest(string? Name, string? Voen, Guid? LogoFileId, string? BrandColor, string? ContactEmail)
    {
        public CorporateDetails ToDetails() => new(Name ?? string.Empty, Voen ?? string.Empty, LogoFileId, BrandColor, ContactEmail ?? string.Empty);
    }

    public sealed record AssignUserRequest(string? Email);
}
