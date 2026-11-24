using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Features.Leaderboard.Queries.GetLeaderboard;
using Qonaqet.Domain.Leaderboard;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>Reytinq lövhələri (app). Landing-in anonim top-u ayrıca <c>public/leaderboard/top</c>-dur.</summary>
[ApiController]
[Route("api/v1/leaderboard")]
public sealed class LeaderboardController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] ScoreSubjectType subjectType, [FromQuery] LeaderboardPeriodType periodType, [FromQuery] string? periodKey, CancellationToken cancellationToken) =>
        (await sender.Send(new GetLeaderboardQuery(subjectType, periodType, periodKey), cancellationToken)).ToActionResult();
}
