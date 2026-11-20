using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Features.Campaigns.Commands;
using Qonaqet.Application.Features.Campaigns.Common;
using Qonaqet.Application.Features.Campaigns.Queries;
using Qonaqet.Domain.Social;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>Admin: kampaniyalar (qaralama → aktiv; bitmə və qalığın köçməsi Worker-dədir).</summary>
[ApiController]
[Route("api/v1/admin/campaigns")]
public sealed class AdminCampaignsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] CampaignStatus? status, CancellationToken cancellationToken) =>
        (await sender.Send(new ListCampaignsQuery(status), cancellationToken)).ToActionResult();

    [HttpPost]
    public async Task<IActionResult> Create(CampaignInput input, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateCampaignCommand(input), cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult();
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, CampaignInput input, CancellationToken cancellationToken) =>
        (await sender.Send(new UpdateCampaignCommand(CampaignId.From(id), input), cancellationToken)).ToActionResult();

    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken) =>
        (await sender.Send(new ActivateCampaignCommand(CampaignId.From(id)), cancellationToken)).ToActionResult();
}
