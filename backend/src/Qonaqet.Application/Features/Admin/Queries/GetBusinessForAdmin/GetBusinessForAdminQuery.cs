using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Businesses.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Admin.Queries.GetBusinessForAdmin;

/// <summary>Admin: bir biznesin tam məlumatı (müraciətə baxış).</summary>
[RequiresRole(RoleNames.Admin)]
public sealed record GetBusinessForAdminQuery(BusinessId BusinessId) : IQuery<BusinessDto>;

public sealed class GetBusinessForAdminHandler(IBusinessRepository businesses) : IQueryHandler<GetBusinessForAdminQuery, BusinessDto>
{
    public async Task<Result<BusinessDto>> Handle(GetBusinessForAdminQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var business = await businesses.GetAsync(request.BusinessId, cancellationToken).ConfigureAwait(false);
        return business is null ? BusinessErrors.NotFound : business.ToDto();
    }
}
