using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Payments.Commands.CompleteFakePayment;
using Qonaqet.Application.Features.Payments.Queries.GetFakeCheckout;
using Qonaqet.Application.Features.Payments.Queries.GetPayment;
using Qonaqet.Domain.Payments;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>Ödənişlər: status, provayder callback-i və Development-in fake ödəniş səhifəsi.</summary>
[ApiController]
[Route("api/v1/payments")]
public sealed class PaymentsController(ISender sender) : ControllerBase
{
    /// <summary>Ödəyicinin öz ödənişi: tətbiq ödəniş səhifəsindən sonra statusu buradan sorğulayır.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        (await sender.Send(new GetPaymentQuery(PaymentId.From(id)), cancellationToken)).ToActionResult();

    /// <summary>
    /// Fake ödəniş səhifəsi (yalnız Development; başqa mühitdə 404). <c>?result=paid</c> ödənişi uğurlu, <c>?result=failed</c>
    /// uğursuz edir; parametrsiz sadə səhifə göstərir.
    /// </summary>
    [HttpGet("fake/{orderId}")]
    public async Task<IActionResult> FakeCheckout(string orderId, [FromQuery] string? result, CancellationToken cancellationToken)
    {
        if (result is not null)
        {
            var paid = string.Equals(result, "paid", StringComparison.OrdinalIgnoreCase);
            var completed = await sender.Send(new CompleteFakePaymentCommand(orderId, paid), cancellationToken);
            if (completed.IsFailure)
            {
                return completed.ToActionResult();
            }
        }

        var order = await sender.Send(new GetFakeCheckoutQuery(orderId), cancellationToken);
        return order.IsSuccess ? Content(Page(order.Value), "text/html; charset=utf-8") : order.ToActionResult();
    }

    private static string Page(FakeOrderView order)
    {
        var amount = (order.AmountQepik / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        var id = WebUtility.HtmlEncode(order.ProviderOrderId);
        var body = order.State switch
        {
            GatewayPaymentState.Paid => "<p>Ödəniş uğurludur. Tətbiqə qayıda bilərsiniz.</p>",
            GatewayPaymentState.Failed => "<p>Ödəniş uğursuz oldu.</p>",
            _ => $"<p><a href=\"?result=paid\">Ödə</a> · <a href=\"?result=failed\">İmtina et</a></p>",
        };
        return $"""
            <!doctype html><html lang="az"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>QONAQET · Test ödənişi</title></head>
            <body><h1>Test ödənişi</h1><p>Sifariş: {id}</p><p>{WebUtility.HtmlEncode(order.Description)} — {amount} AZN</p>{body}
            <p><small>Bu, real ödəniş deyil (Development).</small></p></body></html>
            """;
    }
}
