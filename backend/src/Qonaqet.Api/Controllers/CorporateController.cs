using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Qonaqet.Application.Features.Corporate.Commands.CorporateOrders;
using Qonaqet.Application.Features.Corporate.Commands.CorporatePayments;
using Qonaqet.Application.Features.Corporate.Commands.CorporateReports;
using Qonaqet.Application.Features.Corporate.Queries.CorporateOrders;
using Qonaqet.Application.Features.Corporate.Queries.CorporateReports;
using Qonaqet.Application.Features.Gifts.Common;
using Qonaqet.Domain.Corporate;
using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Social;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>
/// Korporativ portal (<c>CorporateUser</c>): toplu hədiyyə/bonus sifarişləri (şablon, CSV alıcılar, kart və ya bank köçürməsi), hesabatlar və CSR.
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

    [HttpGet("orders/{orderId:guid}/report")]
    public async Task<IActionResult> OrderReport(Guid orderId, CancellationToken cancellationToken) =>
        (await sender.Send(new GetCorporateOrderReportQuery(CorporateGiftOrderId.From(orderId)), cancellationToken)).ToActionResult();

    [HttpGet("orders/{orderId:guid}/report.csv")]
    public async Task<IActionResult> OrderReportCsv(Guid orderId, CancellationToken cancellationToken) =>
        FileResult(await sender.Send(new ExportCorporateOrderReportCsvQuery(CorporateGiftOrderId.From(orderId)), cancellationToken));

    /// <summary>PDF Worker-də yaradılır: cavab sorğudur, hazır olanda <c>GET corporate/reports/{id}/file</c>.</summary>
    [HttpPost("orders/{orderId:guid}/report.pdf")]
    public async Task<IActionResult> OrderReportPdf(Guid orderId, CancellationToken cancellationToken) =>
        Accepted(await sender.Send(new RequestCorporateReportCommand(CorporateReportSubject.GiftOrder, orderId), cancellationToken));

    [HttpGet("campaigns")]
    public async Task<IActionResult> Campaigns(CancellationToken cancellationToken) =>
        (await sender.Send(new ListCorporateCampaignsQuery(), cancellationToken)).ToActionResult();

    [HttpGet("campaigns/{campaignId:guid}/report")]
    public async Task<IActionResult> CampaignReport(Guid campaignId, CancellationToken cancellationToken) =>
        (await sender.Send(new GetCampaignReportQuery(CampaignId.From(campaignId)), cancellationToken)).ToActionResult();

    [HttpGet("campaigns/{campaignId:guid}/report.csv")]
    public async Task<IActionResult> CampaignReportCsv(Guid campaignId, CancellationToken cancellationToken) =>
        FileResult(await sender.Send(new ExportCampaignReportCsvQuery(CampaignId.From(campaignId)), cancellationToken));

    [HttpPost("campaigns/{campaignId:guid}/report.pdf")]
    public async Task<IActionResult> CampaignReportPdf(Guid campaignId, CancellationToken cancellationToken) =>
        Accepted(await sender.Send(new RequestCorporateReportCommand(CorporateReportSubject.Campaign, campaignId), cancellationToken));

    [HttpGet("reports/{reportId:guid}")]
    public async Task<IActionResult> Report(Guid reportId, CancellationToken cancellationToken) =>
        (await sender.Send(new GetCorporateReportQuery(CorporateReportId.From(reportId)), cancellationToken)).ToActionResult();

    [HttpGet("reports/{reportId:guid}/file")]
    public async Task<IActionResult> ReportFile(Guid reportId, CancellationToken cancellationToken) =>
        FileResult(await sender.Send(new DownloadCorporateReportQuery(CorporateReportId.From(reportId)), cancellationToken));

    private IActionResult FileResult(SharedKernel.Result<ReportFileDto> result) =>
        result.IsSuccess ? File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : result.ToActionResult();

    private IActionResult Accepted(SharedKernel.Result<CorporateReportDto> result) =>
        result.IsSuccess ? StatusCode(StatusCodes.Status202Accepted, result.Value) : result.ToActionResult();

    public sealed record CreateOrderRequest(
        GiftKind Kind, GiftScope Scope, Guid? BusinessId, IReadOnlyList<GiftItemRequest>? Items, long? AmountQepik, string? Message, DateTimeOffset? DeliverAt,
        GiftExpiryAction? ExpiryAction);

    public sealed record PayRequest(CorporatePaymentMethod Method);
}
