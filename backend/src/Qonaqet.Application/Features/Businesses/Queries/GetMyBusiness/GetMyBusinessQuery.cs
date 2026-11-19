using Qonaqet.Application.Features.Businesses.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Businesses.Queries.GetMyBusiness;

/// <summary>Üzv olduğu biznesin daxili görünüşü (<c>GET business/{businessId}</c>); VÖEN və IBAN yalnız sahib və adminə.</summary>
public sealed record GetMyBusinessQuery(BusinessId BusinessId) : IQuery<BusinessDto>;

public sealed class GetMyBusinessHandler(BusinessAccess access) : IQueryHandler<GetMyBusinessQuery, BusinessDto>
{
    public async Task<Result<BusinessDto>> Handle(GetMyBusinessQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var grant = await access.RequireAsync(request.BusinessId, BusinessPermission.Operate, cancellationToken).ConfigureAwait(false);
        if (grant.IsFailure)
        {
            return grant.Error;
        }

        return grant.Value.Business.ToDto(grant.Value.SeesFinancials);
    }
}
