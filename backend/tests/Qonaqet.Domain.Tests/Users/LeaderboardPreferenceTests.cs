using Qonaqet.Domain.Users;

namespace Qonaqet.Domain.Tests.Users;

public sealed class LeaderboardPreferenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void New_user_is_on_the_board_as_qonaq_until_the_name_step()
    {
        var user = User.Register("a@example.az", Now);

        Assert.True(user.LeaderboardOptIn);
        Assert.Equal("Qonaq", user.LeaderboardDisplayName);
    }

    [Theory]
    [InlineData("Aysel", "Məmmədova", "Aysel M.")]
    [InlineData("Orxan", null, "Orxan")]
    [InlineData("Əli", "Şükürov", "Əli Ş.")]
    public void Display_name_is_first_name_and_last_initial(string firstName, string? lastName, string expected)
    {
        var user = User.Register("a@example.az", Now);
        user.UpdateProfile(firstName, lastName, null, Now);

        Assert.Equal(expected, user.LeaderboardDisplayName);
    }

    [Fact]
    public void Nickname_replaces_the_name_and_opt_out_keeps_it()
    {
        var user = User.Register("a@example.az", Now);
        user.UpdateProfile("Leyla", "Həsənova", null, Now);

        user.SetLeaderboardPreferences(optIn: false, "  Səxavətli ", Now);

        Assert.Equal((false, "Səxavətli", "Səxavətli"), (user.LeaderboardOptIn, user.LeaderboardName, user.LeaderboardDisplayName));

        user.SetLeaderboardPreferences(optIn: true, null, Now);
        Assert.Equal("Leyla H.", user.LeaderboardDisplayName);
        Assert.Throws<ArgumentOutOfRangeException>(() => user.SetLeaderboardPreferences(true, "x", Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => user.SetLeaderboardPreferences(true, new string('a', User.LeaderboardNameMaxLength + 1), Now));
    }
}
