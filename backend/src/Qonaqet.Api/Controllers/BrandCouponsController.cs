using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Features.Plus.Commands.ValidateCouponCode;
using Qonaqet.Application.Features.Plus.Queries.GetBrandCoupons;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>Brend istifadəçisi: öz kuponlarının statistikası və müştəri kodunun yoxlanması.</summary>
[ApiController]
[Route("api/v1/brand/coupons")]
public sealed class BrandCouponsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        (await sender.Send(new GetBrandCouponsQuery(), cancellationToken)).ToActionResult();

    /// <summary>Kodu yoxlayır; unikal kod istifadə olunmuş kimi işarələnir.</summary>
    [HttpPost("validate")]
    public async Task<IActionResult> Validate(ValidateCodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (await sender.Send(new ValidateCouponCodeCommand(request.Code ?? string.Empty), cancellationToken)).ToActionResult();
    }

    public sealed record ValidateCodeRequest(string? Code);
}
