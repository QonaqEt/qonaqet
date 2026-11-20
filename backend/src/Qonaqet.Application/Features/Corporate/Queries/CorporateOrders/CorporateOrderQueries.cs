using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Features.Corporate.Common;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Corporate.Queries.CorporateOrders;

/// <summary><c>GET corporate/me</c>: cari istifadəçinin şirkəti (portalın başlığı, brendinq).</summary>
[RequiresRole(RoleNames.CorporateUser)]
public sealed record GetMyCorporateQuery() : IQuery<CorporateDto>;

public sealed class GetMyCorporateHandler(CorporateAccess access) : IQueryHandler<GetMyCorporateQuery, CorporateDto>
{
    public async Task<Result<CorporateDto>> Handle(GetMyCorporateQuery request, CancellationToken cancellationToken)
    {
        var corporate = await access.RequireAsync(cancellationToken).ConfigureAwait(false);
        return corporate.IsFailure ? corporate.Error : corporate.Value.ToDto();
    }
}
