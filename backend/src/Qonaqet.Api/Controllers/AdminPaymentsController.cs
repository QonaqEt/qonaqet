using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Features.Admin.Commands.RefundPayment;
using Qonaqet.Application.Features.Admin.Queries.GetAdminPayment;
using Qonaqet.Domain.Payments;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>Admin: ödənişlər və refundlar (<c>Idempotency-Key</c> refundda məcburidir).</summary>
[ApiController]
[Route("api/v1/admin/payments")]
public sealed class AdminPaymentsController(ISender sender) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        (await sender.Send(new GetAdminPaymentQuery(PaymentId.From(id)), cancellationToken)).ToActionResult();

    [HttpPost("{id:guid}/refunds")]
    public async Task<IActionResult> Refund(Guid id, RefundRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RefundPaymentCommand(PaymentId.From(id), request.AmountQepik, request.Reason), cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, new { refundId = result.Value }) : result.ToActionResult();
    }

    public sealed record RefundRequest(long? AmountQepik, string Reason);
}
