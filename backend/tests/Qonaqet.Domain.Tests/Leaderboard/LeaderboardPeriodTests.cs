using Qonaqet.Domain.Leaderboard;

namespace Qonaqet.Domain.Tests.Leaderboard;

public sealed class LeaderboardPeriodTests
{
    [Fact]
    public void Week_range_is_the_baku_iso_week()
    {
        Assert.True(LeaderboardPeriod.TryRange(LeaderboardPeriodType.Week, "2026-W48", out var range));

        // Bakı UTC+4: bazar ertəsi 23 noyabr 00:00 = 22 noyabr 20:00 UTC.
        Assert.Equal(
            (new DateTimeOffset(2026, 11, 22, 20, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 29, 20, 0, 0, TimeSpan.Zero)),
            range);
    }

    [Fact]
    public void Month_range_and_all_time()
    {
        Assert.True(LeaderboardPeriod.TryRange(LeaderboardPeriodType.Month, "2026-12", out var month));
        Assert.Equal(
            (new DateTimeOffset(2026, 11, 30, 20, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 12, 31, 20, 0, 0, TimeSpan.Zero)),
            month);

        Assert.True(LeaderboardPeriod.TryRange(LeaderboardPeriodType.AllTime, "all", out var all));
        Assert.Null(all);
    }

    [Theory]
    [InlineData(LeaderboardPeriodType.Week, "2026-11")]
    [InlineData(LeaderboardPeriodType.Week, "2026-W00")]
    [InlineData(LeaderboardPeriodType.Week, "2026-W54")]
    [InlineData(LeaderboardPeriodType.Week, "2027-W53")] // 2027-də 52 həftə var
    [InlineData(LeaderboardPeriodType.Month, "2026-13")]
    [InlineData(LeaderboardPeriodType.Month, "2026-W48")]
    [InlineData(LeaderboardPeriodType.Month, "26-11")]
    [InlineData(LeaderboardPeriodType.AllTime, "2026")]
    [InlineData(LeaderboardPeriodType.Week, "")]
    public void Invalid_keys_are_rejected(LeaderboardPeriodType type, string key)
    {
        Assert.False(LeaderboardPeriod.IsValidKey(type, key));
    }

    [Fact]
    public void Current_keys_are_valid_and_their_range_contains_the_instant()
    {
        var at = new DateTimeOffset(2026, 11, 29, 20, 30, 0, TimeSpan.Zero);
        foreach (var period in LeaderboardPeriod.For(at).Where(p => p.Type != LeaderboardPeriodType.AllTime))
        {
            Assert.True(LeaderboardPeriod.TryRange(period.Type, period.Key, out var range));
            Assert.InRange(at, range!.Value.Start, range.Value.End.AddTicks(-1));
        }
    }

    [Fact]
    public void Entry_correction_reports_whether_anything_changed()
    {
        var at = new DateTimeOffset(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);
        var entry = LeaderboardEntry.Create(ScoreSubjectType.User, Guid.CreateVersion7(), LeaderboardPeriod.AllTime, 5, 2, 9_00, at);

        Assert.False(entry.Correct(5, 2, 9_00, at.AddHours(1)));
        Assert.Equal(at, entry.UpdatedAt);
        Assert.True(entry.Correct(4, 2, 9_00, at.AddHours(1)));
        Assert.Equal((4, at.AddHours(1)), (entry.Points, entry.UpdatedAt));
    }
}
