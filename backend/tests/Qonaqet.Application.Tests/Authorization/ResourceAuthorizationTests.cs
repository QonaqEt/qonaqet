using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Domain.Users;

namespace Qonaqet.Application.Tests.Authorization;

/// <summary>Handler-lərdə resurs yoxlaması (E02-T03).</summary>
public sealed class ResourceAuthorizationTests
{
    private static readonly Guid Corporate = Guid.CreateVersion7();

    [Theory]
    [InlineData(RoleNames.CorporateUser, true, true)]
    [InlineData(RoleNames.CorporateUser, false, false)]
    [InlineData(RoleNames.BusinessOwner, true, false)]
    [InlineData(RoleNames.Admin, false, true)]
    public void Corporate_users_can_act_only_for_their_own_company(string role, bool sameCompany, bool expected)
    {
        var actor = new Actor([role], null, sameCompany ? Corporate : Guid.CreateVersion7());

        Assert.Equal(expected, actor.CanActForCorporate(Corporate));
    }

    [Fact]
    public void Role_names_match_the_role_enum()
    {
        var constants = typeof(RoleNames).GetFields().Select(f => (string)f.GetRawConstantValue()!).Order();

        Assert.Equal(Enum.GetNames<Role>().Order(), constants);
    }

    [Fact]
    public void Setting_roles_always_keeps_the_user_role()
    {
        var user = User.Register("a@b.az", DateTimeOffset.UtcNow);

        user.SetRoles([Role.Admin, Role.Admin, Role.BusinessOwner], DateTimeOffset.UtcNow, null);

        Assert.Equal([Role.User, Role.BusinessOwner, Role.Admin], user.Roles);
    }

    private sealed class Actor(string[] roles, Guid? businessId, Guid? corporateId) : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public Guid? ActorId { get; } = Guid.CreateVersion7();

        public IReadOnlySet<string> Roles { get; } = roles.ToHashSet();

        public Guid? BusinessId { get; } = businessId;

        public Guid? CorporateId { get; } = corporateId;
    }
}
