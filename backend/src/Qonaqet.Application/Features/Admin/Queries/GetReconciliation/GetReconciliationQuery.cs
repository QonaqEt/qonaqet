using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Ledger.Common;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Admin.Queries.GetReconciliation;

/// <summary><c>GET admin/ledger/reconciliation</c>: son gecəlik uzlaşdırmanın hesabatı (fərqlər).</summary>
[RequiresRole(RoleNames.Admin)]
public sealed record GetReconciliationQuery : IQuery<ReconciliationReportDto>;

public sealed class GetReconciliationHandler(ILedgerRepository ledger) : IQueryHandler<GetReconciliationQuery, ReconciliationReportDto>
{
    public async Task<Result<ReconciliationReportDto>> Handle(GetReconciliationQuery request, CancellationToken cancellationToken)
    {
        var latest = await ledger.LatestReconciliationAsync(cancellationToken).ConfigureAwait(false);
        if (latest is not { } report)
        {
            return new ReconciliationReportDto(null, 0, []);
        }

        return new ReconciliationReportDto(
            report.Run.CreatedAt,
            report.Run.AccountsChecked,
            [.. report.Mismatches.Select(m => new ReconciliationMismatchDto(m.AccountType, m.AccountId, m.OperationalBalance, m.LedgerBalance, m.Difference))]);
    }
}
