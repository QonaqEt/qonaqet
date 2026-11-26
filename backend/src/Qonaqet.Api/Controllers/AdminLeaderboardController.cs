using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Features.Admin.Commands.RecomputeLeaderboard;
using Qonaqet.Application.Features.Admin.Commands.UpdateScorePoints;
using Qonaqet.Application.Features.Admin.Queries.GetScorePoints;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>Admin: lövhə dövrünü <c>score_events</c>-dən yenidən hesablama və xal dəyərlərinin tənzimlənməsi.</summary>
[ApiController]
[Route("api/v1/admin/leaderboard")]
public sealed class AdminLeaderboardController(ISender sender) : ControllerBase
{
    [HttpPost("recompute")]
    public async Task<IActionResult> Recompute(RecomputeLeaderboardCommand command, CancellationToken cancellationToken) =>
        (await sender.Send(command, cancellationToken)).ToActionResult();

    [HttpGet("points")]
    public async Task<IActionResult> GetPoints(CancellationToken cancellationToken) =>
        (await sender.Send(new GetScorePointsQuery(), cancellationToken)).ToActionResult();

    [HttpPut("points")]
    public async Task<IActionResult> UpdatePoints(UpdateScorePointsCommand command, CancellationToken cancellationToken) =>
        (await sender.Send(command, cancellationToken)).ToActionResult();
}
