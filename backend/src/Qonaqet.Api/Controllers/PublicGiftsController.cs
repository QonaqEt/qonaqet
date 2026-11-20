using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Qonaqet.Application.Features.Gifts.Commands.OpenPublicGift;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>
/// İctimai hədiyyə səhifəsinin API-si (<c>/g/{token}</c>, login tələb etmir). Token bearer-dir: endpoint rate-limitlidir, token heç vaxt
/// loglanmır (trace-lərdə yol maskalanır, <see cref="Infrastructure.Observability.SensitivePathRedactor"/>).
/// </summary>
[ApiController]
[Route("api/v1/public/gifts")]
[EnableRateLimiting(RateLimitPolicies.GiftLink)]
public sealed class PublicGiftsController(ISender sender) : ControllerBase
{
    [HttpGet("{token}")]
    public async Task<IActionResult> Get(string token, CancellationToken cancellationToken) =>
        (await sender.Send(new OpenPublicGiftCommand(token), cancellationToken)).ToActionResult();
}
