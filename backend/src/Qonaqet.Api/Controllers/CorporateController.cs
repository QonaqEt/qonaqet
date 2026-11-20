using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Qonaqet.Application.Features.Corporate.Commands.CorporateOrders;
using Qonaqet.Application.Features.Corporate.Commands.CorporatePayments;
using Qonaqet.Application.Features.Corporate.Queries.CorporateOrders;
using Qonaqet.Application.Features.Gifts.Common;
using Qonaqet.Domain.Corporate;
using Qonaqet.Domain.Gifts;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>
/// Korporativ portal (<c>CorporateUser</c>): toplu hədiyyə/bonus sifarişləri (şablon, CSV alıcılar, kart və ya bank köçürməsi).
/// Şirkət cari istifadəçinin hesabından oxunur; başqa şirkətin sifarişi görünmür.
/// </summary>
[ApiController]
[Route("api/v1/corporate")]
public sealed class CorporateController(ISender sender) : ControllerBase
{
    /// <summary>CSV-nin maksimum ölçüsü (5,000 sətir üçün kifayətdir).</summary>
    public const long MaxCsvBytes = 5L * 1024 * 1024;

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken) =>
        (await sender.Send(new GetMyCorporateQuery(), cancellationToken)).ToActionResult();

    [HttpPost("orders")]
    public async Task<IActionResult> CreateOrder(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.Send(
            new CreateCorporateOrderCommand(
                new GiftSelection(request.Kind, request.Scope, request.BusinessId, request.Items, request.AmountQepik), request.Message, request.DeliverAt,
                request.ExpiryAction ?? GiftExpiryAction.RefundToPayer),
            cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult();
    }

    [HttpGet("orders")]
    public async Task<IActionResult> Orders(CancellationToken cancellationToken) =>
        (await sender.Send(new ListCorporateOrdersQuery(), cancellationToken)).ToActionResult();

    [HttpGet("orders/{orderId:guid}")]
    public async Task<IActionResult> Order(Guid orderId, CancellationToken cancellationToken) =>
        (await sender.Send(new GetCorporateOrderQuery(CorporateGiftOrderId.From(orderId)), cancellationToken)).ToActionResult();

    /// <summary>Alıcı siyahısı: multipart <c>file</c> (CSV <c>name,email</c>). Cavab sətir-sətir xəta hesabatıdır.</summary>
    [HttpPost("orders/{orderId:guid}/recipients")]
    [RequestSizeLimit(MaxCsvBytes + 64 * 1024)]
    public async Task<IActionResult> Recipients(Guid orderId, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0 || file.Length > MaxCsvBytes)
        {
            return BadRequest(Application.Common.Errors.CorporateErrors.CsvInvalid.ToProblemDetails());
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        return (await sender.Send(new UploadCorporateRecipientsCommand(CorporateGiftOrderId.From(orderId), buffer.ToArray(), file.FileName), cancellationToken))
            .ToActionResult();
    }

    /// <summary>Ödəniş (<c>Idempotency-Key</c> məcburi): <c>Card</c> → <c>paymentUrl</c>; <c>BankTransfer</c> → köçürmə referansı.</summary>
    [HttpPost("orders/{orderId:guid}/pay")]
    [EnableRateLimiting(RateLimitPolicies.Payment)]
    public async Task<IActionResult> Pay(Guid orderId, PayRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (await sender.Send(new PayCorporateOrderCommand(CorporateGiftOrderId.From(orderId), request.Method), cancellationToken)).ToActionResult();
    }

    [HttpPost("orders/{orderId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid orderId, CancellationToken cancellationToken) =>
        (await sender.Send(new CancelCorporateOrderCommand(CorporateGiftOrderId.From(orderId)), cancellationToken)).ToActionResult();

    public sealed record CreateOrderRequest(
        GiftKind Kind, GiftScope Scope, Guid? BusinessId, IReadOnlyList<GiftItemRequest>? Items, long? AmountQepik, string? Message, DateTimeOffset? DeliverAt,
        GiftExpiryAction? ExpiryAction);

    public sealed record PayRequest(CorporatePaymentMethod Method);
}
