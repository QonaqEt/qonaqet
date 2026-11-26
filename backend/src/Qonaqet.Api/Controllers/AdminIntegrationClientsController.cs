using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Features.Integration.Commands.AdminIntegrationClients;
using Qonaqet.Domain.Integration;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>Admin: Partner API client-ləri — yarat (secret bir dəfə), secret rotasiyası, ləğv (soft delete), siyahı.</summary>
[ApiController]
[Route("api/v1/admin/integration-clients")]
public sealed class AdminIntegrationClientsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] IntegrationOwnerType? ownerType, [FromQuery] Guid? ownerId, CancellationToken cancellationToken) =>
        (await sender.Send(new ListIntegrationClientsQuery(ownerType, ownerId), cancellationToken)).ToActionResult();

    [HttpPost]
    public async Task<IActionResult> Create(ClientRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(
            new CreateIntegrationClientCommand(
                request.Name ?? string.Empty, request.OwnerType, request.OwnerId, request.BusinessIds ?? [], request.Scopes ?? [], request.IsSandbox,
                request.RateLimitPerMinute ?? IntegrationClient.DefaultRateLimit),
            cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult();
    }

    [HttpPost("{clientId:guid}/rotate-secret")]
    public async Task<IActionResult> RotateSecret(Guid clientId, CancellationToken cancellationToken) =>
        (await sender.Send(new RotateIntegrationClientSecretCommand(IntegrationClientId.From(clientId)), cancellationToken)).ToActionResult();

    /// <summary>Ləğv = soft delete; client-in token-ləri dərhal işləməz olur.</summary>
    [HttpDelete("{clientId:guid}")]
    public async Task<IActionResult> Revoke(Guid clientId, CancellationToken cancellationToken) =>
        (await sender.Send(new RevokeIntegrationClientCommand(IntegrationClientId.From(clientId)), cancellationToken)).ToActionResult();

    public sealed record ClientRequest(
        string? Name,
        IntegrationOwnerType OwnerType,
        Guid OwnerId,
        IReadOnlyList<Guid>? BusinessIds,
        IReadOnlyList<string>? Scopes,
        bool IsSandbox,
        int? RateLimitPerMinute);
}
