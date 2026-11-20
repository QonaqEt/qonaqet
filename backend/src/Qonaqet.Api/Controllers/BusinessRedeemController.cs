using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Qonaqet.Application.Features.Social.Commands.RedeemCode;
using Qonaqet.Application.Features.Social.Queries.PreviewCode;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Catalog;
using Qonaqet.Domain.Gifts;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>
/// Biznes paneli: kod yoxlama və təhvil ("Kod yoxla / Təhvil ver"). Kod həmişə body-də gəlir (URL/log-a düşmür); endpoint-lər
/// rate-limitlidir. İşçi yalnız məhsulu, statusu və məbləği görür; hədiyyədə qalan sətirləri/məbləği görüb nəyi təhvil verəcəyini seçir.
/// </summary>
[ApiController]
[Route("api/v1/business/{businessId:guid}/redeem")]
[EnableRateLimiting(RateLimitPolicies.Redeem)]
public sealed class BusinessRedeemController(ISender sender) : ControllerBase
{
    [HttpPost("preview")]
    public async Task<IActionResult> Preview(Guid businessId, CodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (await sender.Send(new PreviewCodeQuery(BusinessId.From(businessId), request.Code), cancellationToken)).ToActionResult();
    }

    [HttpPost]
    public async Task<IActionResult> Redeem(Guid businessId, CodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (await sender.Send(new RedeemCodeCommand(BusinessId.From(businessId), request.Code, Lines(request), request.AmountQepik), cancellationToken))
            .ToActionResult();
    }

    private static List<GiftLine>? Lines(CodeRequest request) =>
        request.Lines?.Select(l => new GiftLine(CatalogItemId.From(l.CatalogItemId), l.Qty)).ToList();

    /// <param name="Lines">Hədiyyədə təhvil veriləcək məhsul sətirləri (məhsul hədiyyəsi).</param>
    /// <param name="AmountQepik">Hədiyyədə təhvil veriləcək məbləğ (vauçer, qismən ola bilər).</param>
    public sealed record CodeRequest(string Code, IReadOnlyList<LineRequest>? Lines = null, long? AmountQepik = null);

    public sealed record LineRequest(Guid CatalogItemId, int Qty);
}
