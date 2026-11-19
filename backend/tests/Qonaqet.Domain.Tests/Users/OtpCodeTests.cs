using Qonaqet.Domain.Users;

namespace Qonaqet.Domain.Tests.Users;

public sealed class OtpCodeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Code_is_usable_for_ten_minutes()
    {
        var otp = OtpCode.Issue(" A@B.AZ ", "hash", Now);

        Assert.Equal("a@b.az", otp.Email);
        Assert.True(otp.IsUsable(Now.AddMinutes(9)));
        Assert.False(otp.IsUsable(Now.AddMinutes(10)));
    }

    [Fact]
    public void New_user_has_the_user_role_and_an_incomplete_profile()
    {
        var user = User.Register("Leyla@Example.AZ", Now);

        Assert.Equal("leyla@example.az", user.Email);
        Assert.Equal([Role.User], user.Roles);
        Assert.False(user.ProfileCompleted);
    }
}
