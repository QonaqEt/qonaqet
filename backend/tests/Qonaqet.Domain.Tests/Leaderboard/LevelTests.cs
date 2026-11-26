using Qonaqet.Domain.Leaderboard;

namespace Qonaqet.Domain.Tests.Leaderboard;

public sealed class LevelTests
{
    [Theory]
    [InlineData(-3, "Qonaq", "Qonaqpərvər")]
    [InlineData(0, "Qonaq", "Qonaqpərvər")]
    [InlineData(49, "Qonaq", "Qonaqpərvər")]
    [InlineData(50, "Qonaqpərvər", "Süfrə sahibi")]
    [InlineData(200, "Süfrə sahibi", "El ağsaqqalı")]
    [InlineData(999, "Süfrə sahibi", "El ağsaqqalı")]
    [InlineData(1000, "El ağsaqqalı", "Səxavət ustası")]
    [InlineData(5000, "Səxavət ustası", null)]
    [InlineData(80_000, "Səxavət ustası", null)]
    public void Level_follows_all_time_points(int points, string level, string? next)
    {
        Assert.Equal(level, Level.For(points).Name);
        Assert.Equal(next, Level.Next(points)?.Name);
    }

    [Fact]
    public void Previous_period_is_the_last_finished_baku_week_and_month()
    {
        var mondayJustAfterMidnight = new DateTimeOffset(2026, 11, 29, 20, 30, 0, TimeSpan.Zero); // Bakı: 30 noyabr 00:30 (W49)
        var firstOfDecember = new DateTimeOffset(2026, 11, 30, 20, 5, 0, TimeSpan.Zero); // Bakı: 1 dekabr 00:05

        Assert.Equal("2026-W48", LeaderboardPeriod.Previous(LeaderboardPeriodType.Week, mondayJustAfterMidnight).Key);
        Assert.Equal("2026-10", LeaderboardPeriod.Previous(LeaderboardPeriodType.Month, mondayJustAfterMidnight).Key);
        Assert.Equal("2026-11", LeaderboardPeriod.Previous(LeaderboardPeriodType.Month, firstOfDecember).Key);
        Assert.Equal("2025-12", LeaderboardPeriod.Previous(LeaderboardPeriodType.Month, new DateTimeOffset(2026, 1, 10, 9, 0, 0, TimeSpan.Zero)).Key);
    }

    [Fact]
    public void Badge_places_are_three_per_week_and_ten_per_month()
    {
        var at = new DateTimeOffset(2026, 11, 30, 9, 0, 0, TimeSpan.Zero);

        Assert.Equal((LeaderboardPeriodType.Week, 3), BadgeAward.Rule(BadgeType.WeekTop3));
        Assert.Equal((LeaderboardPeriodType.Month, 10), BadgeAward.Rule(BadgeType.MonthTop10));
        Assert.Throws<ArgumentOutOfRangeException>(() => BadgeAward.Award(ScoreSubjectType.User, Guid.CreateVersion7(), BadgeType.WeekTop3, "2026-W48", 4, 9, at));
    }
}
