using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Qonaqet.Application.Features.Integration.Commands.PartnerRedeem;
using Qonaqet.Application.Features.Integration.Commands.PartnerReverse;
using Qonaqet.Application.Common;
using Qonaqet.Application.Features.Integration.Queries.ListPartnerRedemptions;
using Qonaqet.Application.Features.Integration.Queries.PreviewPartnerCode;
using Qonaqet.Domain.Gifts;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Integration.Api.Controllers;

/// <summary>Kassa/POS: kodun önizlənməsi və təhvili (işçi təhvili ilə eyni qaydalar).</summary>
[ApiController]
[Route(PartnerRoutes.Prefix)]
[EnableRateLimiting(RateLimitPolicies.Partner)]
public sealed class PartnerRedemptionsController(ISender sender) : ControllerBase
{
    /// <summary>Hədiyyə, claim və ya şəbəkə vauçeri: növ, qalıq, müddət, bu biznesdə təhvil oluna bilərmi.</summary>
    [HttpGet("codes/{code}")]
    public async Task<IActionResult> Preview(string code, [FromQuery] Guid businessId, CancellationToken cancellationToken) =>
        (await sender.Send(new PreviewPartnerCodeQuery(businessId, code), cancellationToken)).ToActionResult();

    /// <summary>Təhvil: məhsul sətirləri (<c>lines</c>) və ya məbləğ (<c>amountQepik</c>, qismən ola bilər). <c>Idempotency-Key</c> məcburidir.</summary>
    [HttpPost("redemptions")]
    public async Task<IActionResult> Redeem(RedeemRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(
            new PartnerRedeemCommand(request.BusinessId, request.Code ?? string.Empty, request.Lines, request.AmountQepik, request.ExternalReference),
            cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult();
    }

    /// <summary>Biznesdəki təhvillər (hədiyyə + claim, geri almalar daxil), ən yenilər əvvəl. Default son 7 gün, ən çox 92 gün.</summary>
    [HttpGet("redemptions")]
    public async Task<IActionResult> List(
        [FromQuery] Guid businessId, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] int? page, [FromQuery] int? pageSize,
        CancellationToken cancellationToken) =>
        (await sender.Send(new ListPartnerRedemptionsQuery(businessId, from, to, PageRequest.Of(page, pageSize)), cancellationToken)).ToActionResult();

    /// <summary>Öz təhvilini 15 dəqiqə ərzində geri alır. <c>Idempotency-Key</c> məcburidir.</summary>
    [HttpPost("redemptions/{redemptionId:guid}/reverse")]
    public async Task<IActionResult> Reverse(Guid redemptionId, CancellationToken cancellationToken) =>
        (await sender.Send(new PartnerReverseRedemptionCommand(redemptionId), cancellationToken)).ToActionResult();

    public sealed record RedeemRequest(Guid BusinessId, string? Code, IReadOnlyList<GiftLine>? Lines, long? AmountQepik, string? ExternalReference);
}
