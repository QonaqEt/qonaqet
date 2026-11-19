using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Qonaqet.Application.Features.Auth.Commands.Logout;
using Qonaqet.Application.Features.Auth.Commands.RefreshSession;
using Qonaqet.Application.Features.Auth.Commands.SendOtp;
using Qonaqet.Application.Features.Auth.Commands.VerifyOtp;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>Email OTP ilə giriş/qeydiyyat. İcazə qaydaları pipeline-dadır (<c>[PublicRequest]</c>).</summary>
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(ISender sender) : ControllerBase
{
    [HttpPost("otp/send")]
    [EnableRateLimiting(RateLimitPolicies.Otp)]
    public async Task<IActionResult> SendOtp(SendOtpCommand command, CancellationToken cancellationToken) =>
        (await sender.Send(command, cancellationToken)).ToActionResult();

    [HttpPost("otp/verify")]
    [EnableRateLimiting(RateLimitPolicies.Otp)]
    public async Task<IActionResult> VerifyOtp(VerifyOtpCommand command, CancellationToken cancellationToken) =>
        (await sender.Send(command, cancellationToken)).ToActionResult();

    [HttpPost("refresh")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> Refresh(RefreshSessionCommand command, CancellationToken cancellationToken) =>
        (await sender.Send(command, cancellationToken)).ToActionResult();

    [HttpPost("logout")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> Logout(LogoutCommand command, CancellationToken cancellationToken) =>
        (await sender.Send(command, cancellationToken)).ToActionResult();
}
