using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Businesses.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Businesses.Queries.ListStaff;

/// <summary>Biznesin üzvləri (sahib və işçilər) — yalnız sahib və admin üçün.</summary>
public sealed record ListStaffQuery(BusinessId BusinessId) : IQuery<IReadOnlyList<StaffMemberDto>>;

public sealed class ListStaffHandler(BusinessAccess access, IBusinessMembershipRepository memberships, IUserRepository users)
    : IQueryHandler<ListStaffQuery, IReadOnlyList<StaffMemberDto>>
{
    public async Task<Result<IReadOnlyList<StaffMemberDto>>> Handle(ListStaffQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var grant = await access.RequireAsync(request.BusinessId, BusinessPermission.Manage, cancellationToken).ConfigureAwait(false);
        if (grant.IsFailure)
        {
            return grant.Error;
        }

        var result = new List<StaffMemberDto>();
        foreach (var membership in await memberships.ListForBusinessAsync(request.BusinessId, cancellationToken).ConfigureAwait(false))
        {
            if (await users.GetAsync(UserId.From(membership.UserId), cancellationToken).ConfigureAwait(false) is { } user)
            {
                result.Add(StaffMemberDto.From(membership, user));
            }
        }

        return Result<IReadOnlyList<StaffMemberDto>>.Success(result);
    }
}
