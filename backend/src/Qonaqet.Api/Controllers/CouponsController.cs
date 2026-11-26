using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Features.Plus.Commands.RevealCoupon;
using Qonaqet.Application.Features.Plus.Queries.GetCoupons;
using Qonaqet.Domain.Plus;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>QONAQET Plus: istifadəçinin kuponları və kodun açılması.</summary>
[ApiController]
[Route("api/v1/coupons")]
public sealed class CouponsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        (await sender.Send(new GetCouponsQuery(), cancellationToken)).ToActionResult();

    [HttpPost("{couponId:guid}/reveal")]
    public async Task<IActionResult> Reveal(Guid couponId, CancellationToken cancellationToken) =>
        (await sender.Send(new RevealCouponCommand(CouponId.From(couponId)), cancellationToken)).ToActionResult();
}
