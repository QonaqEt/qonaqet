using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Features.Integration.Commands.OwnerIntegrationClients;
using Qonaqet.Domain.Integration;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>Biznes portalı → İnteqrasiyalar (E13-T11): sahibin öz kassa/POS client-ləri. İcazə yoxlaması handler-dədir.</summary>
[ApiController]
[Route("api/v1/business/integrations/clients")]
public sealed class BusinessIntegrationClientsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        (await sender.Send(new ListOwnerIntegrationClientsQuery(), cancellationToken)).ToActionResult();

    /// <summary>Client yaradılır; <c>clientSecret</c> yalnız bu cavabda.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(ClientRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(
            new CreateOwnerIntegrationClientCommand(request.Name ?? string.Empty, request.BusinessIds ?? [], request.Scopes ?? [], request.IsSandbox),
            cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult();
    }

    [HttpPost("{clientId:guid}/rotate-secret")]
    public async Task<IActionResult> Rotate(Guid clientId, CancellationToken cancellationToken) =>
        (await sender.Send(new RotateOwnerIntegrationClientSecretCommand(IntegrationClientId.From(clientId)), cancellationToken)).ToActionResult();

    /// <summary>Ləğv = soft delete.</summary>
    [HttpDelete("{clientId:guid}")]
    public async Task<IActionResult> Revoke(Guid clientId, CancellationToken cancellationToken) =>
        (await sender.Send(new RevokeOwnerIntegrationClientCommand(IntegrationClientId.From(clientId)), cancellationToken)).ToActionResult();

    public sealed record ClientRequest(string? Name, IReadOnlyList<Guid>? BusinessIds, IReadOnlyList<string>? Scopes, bool IsSandbox);
}
