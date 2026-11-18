using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Tests.SharedKernel;

public sealed class BakuTimeTests
{
    [Fact]
    public void Baku_is_utc_plus_four()
    {
        var instant = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromHours(4), BakuTime.ToBakuTime(instant).Offset);
    }

    [Theory]
    [InlineData(19, 59, 59, 18)] // 23:59:59 Bakı → hələ 18-i
    [InlineData(20, 0, 0, 19)]   // 00:00 Bakı → artıq 19-u
    public void Day_boundary_is_midnight_in_baku_not_utc(int hour, int minute, int second, int expectedDay)
    {
        var instant = new DateTimeOffset(2026, 11, 18, hour, minute, second, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 11, expectedDay), BakuTime.ToBakuDate(instant));
    }

    [Fact]
    public void Year_boundary_follows_baku_date()
    {
        var newYearInBaku = new DateTimeOffset(2026, 12, 31, 20, 30, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2027, 1, 1), BakuTime.ToBakuDate(newYearInBaku));
    }

    [Fact]
    public void Start_and_end_of_day_are_returned_in_utc()
    {
        var day = new DateOnly(2026, 11, 18);

        var start = BakuTime.StartOfDayUtc(day);
        var end = BakuTime.EndOfDayUtc(day);

        Assert.Equal(new DateTimeOffset(2026, 11, 17, 20, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(TimeSpan.Zero, start.Offset);
        Assert.Equal(TimeSpan.FromDays(1), end - start);
        Assert.Equal(day, BakuTime.ToBakuDate(start));
        Assert.Equal(day, BakuTime.ToBakuDate(end.AddTicks(-1)));
    }

    [Fact]
    public void Clock_gives_utc_and_baku_today()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 18, 21, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 11, 19), clock.BakuToday());
    }

    [Fact]
    public void System_clock_returns_utc()
    {
        Assert.Equal(TimeSpan.Zero, new SystemClock().UtcNow.Offset);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
