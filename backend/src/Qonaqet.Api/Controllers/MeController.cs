using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Features.Leaderboard.Commands.UpdateLeaderboardSettings;
using Qonaqet.Application.Features.Leaderboard.Queries.GetMyScore;
using Qonaqet.Application.Features.Users.Commands.DeleteMe;
using Qonaqet.Application.Features.Users.Commands.RegisterFcmToken;
using Qonaqet.Application.Features.Users.Commands.RemoveFcmToken;
using Qonaqet.Application.Features.Users.Commands.UpdateProfile;
using Qonaqet.Application.Features.Users.Commands.UpdateNotificationPreferences;
using Qonaqet.Application.Features.Users.Queries.GetMe;
using Qonaqet.Application.Features.Users.Queries.GetNotificationPreferences;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>Cari istifadəçinin hesabı. Autentifikasiya tələbi pipeline-dadır (atributsuz sorğular default olaraq tələb edir).</summary>
[ApiController]
[Route("api/v1/me")]
public sealed class MeController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        (await sender.Send(new GetMeQuery(), cancellationToken)).ToActionResult();

    [HttpPatch]
    public async Task<IActionResult> Update(UpdateProfileCommand command, CancellationToken cancellationToken) =>
        (await sender.Send(command, cancellationToken)).ToActionResult();

    [HttpPost("fcm-token")]
    public async Task<IActionResult> RegisterFcmToken(RegisterFcmTokenCommand command, CancellationToken cancellationToken) =>
        (await sender.Send(command, cancellationToken)).ToActionResult();

    [HttpDelete("fcm-token")]
    public async Task<IActionResult> RemoveFcmToken(RemoveFcmTokenCommand command, CancellationToken cancellationToken) =>
        (await sender.Send(command, cancellationToken)).ToActionResult();

    [HttpGet("notifications")]
    public async Task<IActionResult> GetNotifications(CancellationToken cancellationToken) =>
        (await sender.Send(new GetNotificationPreferencesQuery(), cancellationToken)).ToActionResult();

    [HttpPatch("notifications")]
    public async Task<IActionResult> UpdateNotifications(UpdateNotificationPreferencesCommand command, CancellationToken cancellationToken) =>
        (await sender.Send(command, cancellationToken)).ToActionResult();

    [HttpGet("score")]
    public async Task<IActionResult> GetScore(CancellationToken cancellationToken) =>
        (await sender.Send(new GetMyScoreQuery(), cancellationToken)).ToActionResult();

    [HttpPatch("leaderboard")]
    public async Task<IActionResult> UpdateLeaderboard(UpdateLeaderboardSettingsCommand command, CancellationToken cancellationToken) =>
        (await sender.Send(command, cancellationToken)).ToActionResult();

    [HttpDelete]
    public async Task<IActionResult> Delete([FromQuery] string? reason, CancellationToken cancellationToken) =>
        (await sender.Send(new DeleteMeCommand(reason), cancellationToken)).ToActionResult();
}
