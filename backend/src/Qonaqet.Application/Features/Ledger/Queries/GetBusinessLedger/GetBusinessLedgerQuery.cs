using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Common;
using Qonaqet.Application.Features.Businesses.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Ledger;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Ledger.Queries.GetBusinessLedger;

/// <summary>
/// <c>GET business/{businessId}/ledger</c>: biznesin balansı (ledger-dən <c>SUM(amount)</c>, Postgres) və yazıları. Yalnız sahib (və admin):
/// maliyyə məlumatı işçiyə açılmır. Yazılarda alıcı/ianəçi haqqında heç nə yoxdur — yalnız tip, məbləğ və mənbə ID-si.
/// </summary>
public sealed record GetBusinessLedgerQuery(BusinessId BusinessId, int? Page, int? PageSize) : IQuery<BusinessLedgerDto>;

public sealed record LedgerEntryDto(Guid Id, LedgerEntryType Type, long AmountQepik, string RefType, Guid RefId, DateTimeOffset CreatedAt);

public sealed record BusinessLedgerDto(long BalanceQepik, IReadOnlyList<LedgerEntryDto> Items, long Total);

public sealed class GetBusinessLedgerHandler(BusinessAccess access, ILedgerRepository ledger) : IQueryHandler<GetBusinessLedgerQuery, BusinessLedgerDto>
{
    public async Task<Result<BusinessLedgerDto>> Handle(GetBusinessLedgerQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var grant = await access.RequireAsync(request.BusinessId, BusinessPermission.Manage, cancellationToken).ConfigureAwait(false);
        if (grant.IsFailure)
        {
            return grant.Error;
        }

        var account = LedgerAccount.Business(request.BusinessId.Value);
        var balance = await ledger.BalanceAsync(account, cancellationToken).ConfigureAwait(false);
        var page = await ledger.ListAsync(account, PageRequest.Of(request.Page, request.PageSize), cancellationToken).ConfigureAwait(false);
        return new BusinessLedgerDto(
            balance,
            [.. page.Items.Select(e => new LedgerEntryDto(e.Id, e.Type, e.Amount, e.RefType, e.RefId, e.CreatedAt))],
            page.Total);
    }
}
