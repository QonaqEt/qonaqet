using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Features.Businesses.Commands.AddStaff;
using Qonaqet.Application.Features.Businesses.Commands.RemoveStaff;
using Qonaqet.Application.Features.Businesses.Queries.GetMyBusiness;
using Qonaqet.Application.Features.Businesses.Queries.ListStaff;
using Qonaqet.Domain.Businesses;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>
/// Biznes paneli. Biznes həmişə URL-də açıq verilir (<c>/business/{businessId}/…</c>); giriş (sahib/işçi/admin) handler-də
/// üzvlüklə yoxlanılır, başqa biznesə müraciət <c>403</c> alır.
/// </summary>
[ApiController]
[Route("api/v1/business/{businessId:guid}")]
public sealed class BusinessPanelController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid businessId, CancellationToken cancellationToken) =>
        (await sender.Send(new GetMyBusinessQuery(BusinessId.From(businessId)), cancellationToken)).ToActionResult();

    [HttpGet("staff")]
    public async Task<IActionResult> ListStaff(Guid businessId, CancellationToken cancellationToken) =>
        (await sender.Send(new ListStaffQuery(BusinessId.From(businessId)), cancellationToken)).ToActionResult();

    [HttpPost("staff")]
    public async Task<IActionResult> AddStaff(Guid businessId, AddStaffRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(new AddStaffCommand(BusinessId.From(businessId), request.Email), cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult();
    }

    [HttpDelete("staff/{userId:guid}")]
    public async Task<IActionResult> RemoveStaff(Guid businessId, Guid userId, [FromQuery] string? reason, CancellationToken cancellationToken) =>
        (await sender.Send(new RemoveStaffCommand(BusinessId.From(businessId), userId, reason), cancellationToken)).ToActionResult();

    public sealed record AddStaffRequest(string Email);
}
