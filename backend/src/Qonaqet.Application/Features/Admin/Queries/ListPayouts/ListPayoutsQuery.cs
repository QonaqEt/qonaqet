using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Common;
using Qonaqet.Application.Features.Ledger.Common;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Admin.Queries.ListPayouts;

/// <summary><c>GET admin/payouts?businessId=&amp;page=&amp;pageSize=</c>.</summary>
[RequiresRole(RoleNames.Admin)]
public sealed record ListPayoutsQuery(Guid? BusinessId, int? Page, int? PageSize) : IQuery<PagedResult<PayoutDto>>;

public sealed class ListPayoutsHandler(ILedgerRepository ledger) : IQueryHandler<ListPayoutsQuery, PagedResult<PayoutDto>>
{
    public async Task<Result<PagedResult<PayoutDto>>> Handle(ListPayoutsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var page = await ledger.ListPayoutsAsync(request.BusinessId, PageRequest.Of(request.Page, request.PageSize), cancellationToken).ConfigureAwait(false);
        return new PagedResult<PayoutDto>([.. page.Items.Select(p => p.ToDto())], page.Total);
    }
}
