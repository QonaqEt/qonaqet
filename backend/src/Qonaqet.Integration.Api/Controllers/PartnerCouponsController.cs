using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Qonaqet.Application.Features.Integration.Commands.PartnerValidateCoupon;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Integration.Api.Controllers;

/// <summary>QONAQET Plus brendləri: müştərinin kupon kodunu yoxlayıb istifadə olunmuş kimi işarələmək.</summary>
[ApiController]
[Route(PartnerRoutes.Prefix + "/coupons")]
[EnableRateLimiting(RateLimitPolicies.Partner)]
public sealed class PartnerCouponsController(ISender sender) : ControllerBase
{
    /// <summary><c>{couponId, code}</c> → kod etibarlıdırsa işarələnir. <c>Idempotency-Key</c> məcburidir.</summary>
    [HttpPost("validate")]
    public async Task<IActionResult> Validate(ValidateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (await sender.Send(new PartnerValidateCouponCommand(request.CouponId, request.Code ?? string.Empty), cancellationToken)).ToActionResult();
    }

    public sealed record ValidateRequest(Guid CouponId, string? Code);
}
