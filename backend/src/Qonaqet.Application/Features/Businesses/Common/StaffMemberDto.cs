using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Users;

namespace Qonaqet.Application.Features.Businesses.Common;

/// <summary>Biznesin üzvü (yalnız sahib görür).</summary>
public sealed record StaffMemberDto(Guid UserId, string Email, string? FirstName, string? LastName, MembershipRole Role, DateTimeOffset AddedAt)
{
    public static StaffMemberDto From(BusinessMembership membership, User user)
    {
        ArgumentNullException.ThrowIfNull(membership);
        ArgumentNullException.ThrowIfNull(user);
        return new StaffMemberDto(user.Id.Value, user.Email, user.FirstName, user.LastName, membership.Role, membership.CreatedAt);
    }
}
