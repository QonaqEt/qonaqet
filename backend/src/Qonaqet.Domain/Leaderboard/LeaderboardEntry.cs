using System.Globalization;
using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Leaderboard;

/// <summary>Lövhə dövrü: həftəlik, aylıq, bütün zamanlar.</summary>
public enum LeaderboardPeriodType
{
    Week,
    Month,
    AllTime,
}

/// <summary>
/// Dövr və onun açarı, Bakı təqvimi ilə: həftə ISO 8601 (<c>2026-W48</c>), ay <c>2026-11</c>, bütün zamanlar <c>all</c>.
/// </summary>
public readonly record struct LeaderboardPeriod(LeaderboardPeriodType Type, string Key)
{
    public static LeaderboardPeriod AllTime { get; } = new(LeaderboardPeriodType.AllTime, "all");

    /// <summary>Anın düşdüyü üç dövr (xal hadisəsi hər üçünə yazılır).</summary>
    public static IReadOnlyList<LeaderboardPeriod> For(DateTimeOffset at) =>
        [Current(LeaderboardPeriodType.Week, at), Current(LeaderboardPeriodType.Month, at), AllTime];

    /// <summary>
    /// Açarın dövrü əhatə etdiyi UTC aralığı <c>[Start, End)</c>, Bakı təqvimi ilə; bütün zamanlar üçün <c>null</c> (aralıq yoxdur).
    /// Açar düzgün deyilsə <c>false</c>.
    /// </summary>
    public static bool TryRange(LeaderboardPeriodType type, string key, out (DateTimeOffset Start, DateTimeOffset End)? range)
    {
        range = null;
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        switch (type)
        {
            case LeaderboardPeriodType.AllTime:
                return key == AllTime.Key;
            case LeaderboardPeriodType.Week:
                if (key.Length != 8 || key[4] != '-' || key[5] != 'W'
                    || !int.TryParse(key.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var isoYear)
                    || !int.TryParse(key.AsSpan(6, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var week)
                    || isoYear < 2000 || week < 1 || week > ISOWeek.GetWeeksInYear(isoYear))
                {
                    return false;
                }

                var monday = DateOnly.FromDateTime(ISOWeek.ToDateTime(isoYear, week, DayOfWeek.Monday));
                range = (BakuTime.StartOfDayUtc(monday), BakuTime.StartOfDayUtc(monday.AddDays(7)));
                return true;
            case LeaderboardPeriodType.Month:
                if (key.Length != 7 || key[4] != '-'
                    || !int.TryParse(key.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year)
                    || !int.TryParse(key.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var month)
                    || year < 2000 || month < 1 || month > 12)
                {
                    return false;
                }

                var first = new DateOnly(year, month, 1);
                range = (BakuTime.StartOfDayUtc(first), BakuTime.StartOfDayUtc(first.AddMonths(1)));
                return true;
            default:
                return false;
        }
    }

    public static bool IsValidKey(LeaderboardPeriodType type, string key) => TryRange(type, key, out _);

    /// <summary>Anın dövründən əvvəlki (artıq bitmiş) dövr; bütün zamanların əvvəlkisi yoxdur.</summary>
    public static LeaderboardPeriod Previous(LeaderboardPeriodType type, DateTimeOffset at)
    {
        var today = BakuTime.ToBakuDate(at);
        return type switch
        {
            LeaderboardPeriodType.Week => Current(type, BakuTime.StartOfDayUtc(today.AddDays(-7))),
            LeaderboardPeriodType.Month => Current(type, BakuTime.StartOfDayUtc(new DateOnly(today.Year, today.Month, 1).AddDays(-1))),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Bütün zamanların əvvəlki dövrü yoxdur."),
        };
    }

    public static LeaderboardPeriod Current(LeaderboardPeriodType type, DateTimeOffset at)
    {
        var day = BakuTime.ToBakuDate(at).ToDateTime(TimeOnly.MinValue);
        return type switch
        {
            LeaderboardPeriodType.Week => new(type, string.Create(
                CultureInfo.InvariantCulture, $"{ISOWeek.GetYear(day):D4}-W{ISOWeek.GetWeekOfYear(day):D2}")),
            LeaderboardPeriodType.Month => new(type, string.Create(CultureInfo.InvariantCulture, $"{day.Year:D4}-{day.Month:D2}")),
            LeaderboardPeriodType.AllTime => AllTime,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }
}

/// <summary>Bir lövhə: qazanan tipi + dövr. Valkey açarı <c>lb:{subjectType}:{periodType}:{periodKey}</c>.</summary>
public sealed record LeaderboardBoard(ScoreSubjectType SubjectType, LeaderboardPeriodType PeriodType, string PeriodKey)
{
    public string CacheKey => $"lb:{SubjectType}:{PeriodType}:{PeriodKey}";
}

/// <summary>Lövhədə bir qazananın xalı.</summary>
public sealed record LeaderboardScore(Guid SubjectId, int Points);

/// <summary>
/// Lövhə aqreqatı (Postgres <c>leaderboard_entries</c>): qazananın dövr üzrə xal, qonaqlıq və dəyər cəmi. Yalnız score consumer-i
/// <c>INSERT ... ON CONFLICT DO UPDATE</c> ilə yazır; <c>score_events</c>-dən tam yenidən qurula bilər, ona görə həqiqət mənbəyi hadisələrdir.
/// </summary>
public sealed class LeaderboardEntry
{
    private LeaderboardEntry()
    {
    }

    public Guid Id { get; private set; }

    public ScoreSubjectType SubjectType { get; private set; }

    public Guid SubjectId { get; private set; }

    public LeaderboardPeriodType PeriodType { get; private set; }

    public string PeriodKey { get; private set; } = string.Empty;

    public int Points { get; private set; }

    public int Treats { get; private set; }

    public long Value { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Yenidən hesablamada qeydi olmayan qazanan üçün sətir (adi yolda sətirləri consumer-in upsert-i yaradır).</summary>
    public static LeaderboardEntry Create(
        ScoreSubjectType subjectType, Guid subjectId, LeaderboardPeriod period, int points, int treats, long value, DateTimeOffset at) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            SubjectType = subjectType,
            SubjectId = Guard.NotEmpty(subjectId),
            PeriodType = period.Type,
            PeriodKey = Guard.NotNullOrWhiteSpace(period.Key),
            Points = points,
            Treats = treats,
            Value = value,
            UpdatedAt = Guard.Utc(at),
        };

    /// <summary><c>score_events</c>-dən hesablanmış cəmlə düzəldir; cəm eynidirsə heç nə dəyişmir və <c>false</c> qaytarır.</summary>
    public bool Correct(int points, int treats, long value, DateTimeOffset at)
    {
        if (Points == points && Treats == treats && Value == value)
        {
            return false;
        }

        Points = points;
        Treats = treats;
        Value = value;
        UpdatedAt = Guard.Utc(at);
        return true;
    }
}
