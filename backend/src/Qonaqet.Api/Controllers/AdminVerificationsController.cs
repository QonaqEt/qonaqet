using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Features.Admin.Commands.ReviewVerification;
using Qonaqet.Application.Features.Admin.Queries.ListVerifications;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Verification;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>Admin: təsdiq sorğuları növbəsi, təsdiq/rədd və müddətin dəyişdirilməsi. Rol tələbi əmrlərin özündədir.</summary>
[ApiController]
[Route("api/v1/admin/verifications")]
public sealed class AdminVerificationsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] VerificationStatus? status, [FromQuery] Guid? categoryId, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        (await sender.Send(
            new ListVerificationsQuery(status, categoryId is { } c ? CategoryId.From(c) : null, page, pageSize), cancellationToken)).ToActionResult();

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        (await sender.Send(new GetVerificationForAdminQuery(VerificationRequestId.From(id)), cancellationToken)).ToActionResult();

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, ApproveRequest? request, CancellationToken cancellationToken) =>
        (await sender.Send(new ApproveVerificationCommand(VerificationRequestId.From(id), request?.ValidUntil), cancellationToken)).ToActionResult();

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, RejectRequest request, CancellationToken cancellationToken) =>
        (await sender.Send(new RejectVerificationCommand(VerificationRequestId.From(id), request?.Reason ?? string.Empty), cancellationToken)).ToActionResult();

    [HttpPut("{id:guid}/valid-until")]
    public async Task<IActionResult> ChangeValidUntil(Guid id, ValidUntilRequest request, CancellationToken cancellationToken) =>
        (await sender.Send(new ChangeVerificationValidityCommand(VerificationRequestId.From(id), request?.ValidUntil), cancellationToken)).ToActionResult();

    /// <summary><c>validUntil</c> boşdursa kateqoriya qaydası ilə hesablanır.</summary>
    public sealed record ApproveRequest(DateTimeOffset? ValidUntil);

    public sealed record RejectRequest(string Reason);

    /// <summary><c>null</c> = müddətsiz.</summary>
    public sealed record ValidUntilRequest(DateTimeOffset? ValidUntil);
}
