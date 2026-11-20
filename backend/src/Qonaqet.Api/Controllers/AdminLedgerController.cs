using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Qonaqet.Application.Features.Admin.Commands.RecordPayout;
using Qonaqet.Application.Features.Admin.Queries.GetReconciliation;
using Qonaqet.Application.Features.Admin.Queries.ListPayouts;
using Qonaqet.Domain.Businesses;
using Qonaqet.Infrastructure.Web;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Api.Controllers;

/// <summary>Admin: payout-lar (əl ilə bank köçürmələri) və gecəlik ledger uzlaşdırmasının hesabatı.</summary>
[ApiController]
[Route("api/v1/admin")]
public sealed class AdminLedgerController(ISender sender) : ControllerBase
{
    [HttpPost("payouts")]
    public async Task<IActionResult> RecordPayout(RecordPayoutRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RecordPayoutCommand(BusinessId.From(request.BusinessId), request.AmountQepik, request.BankReference), cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, new { payoutId = result.Value }) : result.ToActionResult();
    }

    [HttpGet("payouts")]
    public async Task<IActionResult> ListPayouts([FromQuery] Guid? businessId, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        (await sender.Send(new ListPayoutsQuery(businessId, page, pageSize), cancellationToken)).ToActionResult();

    [HttpGet("ledger/reconciliation")]
    public async Task<IActionResult> Reconciliation(CancellationToken cancellationToken) =>
        (await sender.Send(new GetReconciliationQuery(), cancellationToken)).ToActionResult();

    public sealed record RecordPayoutRequest(Guid BusinessId, long AmountQepik, string BankReference);
}
