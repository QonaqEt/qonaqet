using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Common;
using Qonaqet.Application.Features.Businesses.Common;
using Qonaqet.Application.Features.Ledger.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Ledger.Queries.ListBusinessPayouts;

/// <summary><c>GET business/{businessId}/payouts</c>: biznesə edilmiş köçürmələr; yalnız sahib (və admin).</summary>
public sealed record ListBusinessPayoutsQuery(BusinessId BusinessId, int? Page, int? PageSize) : IQuery<PagedResult<PayoutDto>>;

public sealed class ListBusinessPayoutsHandler(BusinessAccess access, ILedgerRepository ledger) : IQueryHandler<ListBusinessPayoutsQuery, PagedResult<PayoutDto>>
{
    public async Task<Result<PagedResult<PayoutDto>>> Handle(ListBusinessPayoutsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var grant = await access.RequireAsync(request.BusinessId, BusinessPermission.Manage, cancellationToken).ConfigureAwait(false);
        if (grant.IsFailure)
        {
            return grant.Error;
        }

        var page = await ledger.ListPayoutsAsync(request.BusinessId.Value, PageRequest.Of(request.Page, request.PageSize), cancellationToken).ConfigureAwait(false);
        return new PagedResult<PayoutDto>([.. page.Items.Select(p => p.ToDto())], page.Total);
    }
}
