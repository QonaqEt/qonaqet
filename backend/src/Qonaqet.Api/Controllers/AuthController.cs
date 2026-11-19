using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
}
