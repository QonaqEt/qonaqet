using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Common;
using Qonaqet.Application.Features.Businesses.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Admin.Queries.ListBusinesses;

/// <summary>Admin: bizneslər (müraciət növbəsi <c>?status=Pending</c>), ən yenilər əvvəl; <c>includeDeleted</c> silinmişləri də göstərir.</summary>
[RequiresRole(RoleNames.Admin)]
public sealed record ListBusinessesQuery(BusinessStatus? Status, bool IncludeDeleted, int? Page, int? PageSize) : IQuery<PagedResult<BusinessDto>>;

public sealed class ListBusinessesHandler(IBusinessRepository businesses) : IQueryHandler<ListBusinessesQuery, PagedResult<BusinessDto>>
{
    public async Task<Result<PagedResult<BusinessDto>>> Handle(ListBusinessesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var page = await businesses.ListForAdminAsync(request.Status, request.IncludeDeleted, PageRequest.Of(request.Page, request.PageSize), cancellationToken)
            .ConfigureAwait(false);
        return new PagedResult<BusinessDto>([.. page.Items.Select(b => b.ToDto())], page.Total);
    }
}
