using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Features.Campaigns.Queries;
using Qonaqet.Application.Features.PublicSite.Queries.GetPublicStats;
using Qonaqet.Application.Features.Social.Queries.GetPoolsSummary;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>İctimai (anonim) aqreqatlar: landing sayğacları, hovuz xülasəsi, aktiv kampaniyalar.</summary>
[ApiController]
[Route("api/v1")]
public sealed class PublicSiteController(ISender sender) : ControllerBase
{
    [HttpGet("public/stats")]
    public async Task<IActionResult> Stats(CancellationToken cancellationToken) =>
        (await sender.Send(new GetPublicStatsQuery(), cancellationToken)).ToActionResult();

    [HttpGet("pools/summary")]
    public async Task<IActionResult> PoolsSummary(CancellationToken cancellationToken) =>
        (await sender.Send(new GetPoolsSummaryQuery(), cancellationToken)).ToActionResult();

    [HttpGet("campaigns/active")]
    public async Task<IActionResult> ActiveCampaigns(CancellationToken cancellationToken) =>
        (await sender.Send(new GetActiveCampaignsQuery(), cancellationToken)).ToActionResult();
}
