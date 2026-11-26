using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Leaderboard;

/// <summary>Səviyyə, bütün zamanların xalına görə (saxlanılmır, hər oxuda hesablanır — xal düzəlişi səviyyəni də düzəldir).</summary>
public sealed record Level(string Name, int MinPoints)
{
    public static IReadOnlyList<Level> All { get; } =
    [
        new("Qonaq", 0),
        new("Qonaqpərvər", 50),
        new("Süfrə sahibi", 200),
        new("El ağsaqqalı", 1000),
        new("Səxavət ustası", 5000),
    ];

    public static Level For(int points) => All.Last(l => l.MinPoints <= Math.Max(0, points));

    /// <summary>Növbəti səviyyə; ən yüksəkdədirsə <c>null</c>.</summary>
    public static Level? Next(int points) => All.FirstOrDefault(l => l.MinPoints > Math.Max(0, points));
}

/// <summary>Dövr sonunda verilən nişanlar: həftənin ilk 3-ü, ayın ilk 10-u.</summary>
public enum BadgeType
{
    WeekTop3,
    MonthTop10,
}

/// <summary>
/// Verilmiş nişan (Postgres <c>badge_awards</c>, append-only). (<see cref="SubjectType"/>, <see cref="SubjectId"/>, <see cref="Badge"/>,
/// <see cref="PeriodKey"/>) unikaldır: eyni dövr üçün nişan ikiqat verilmir.
/// </summary>
public sealed class BadgeAward : IAppendOnly
{
    private BadgeAward()
    {
    }

    public Guid Id { get; private set; }

    public ScoreSubjectType SubjectType { get; private set; }

    public Guid SubjectId { get; private set; }

    public BadgeType Badge { get; private set; }

    public string PeriodKey { get; private set; } = string.Empty;

    /// <summary>Dövr sonundakı yer (1-dən).</summary>
    public int Rank { get; private set; }

    public int Points { get; private set; }

    public DateTimeOffset AwardedAt { get; private set; }

    /// <summary>Nişanın dövrü və neçə yerə verildiyi.</summary>
    public static (LeaderboardPeriodType PeriodType, int Places) Rule(BadgeType badge) => badge switch
    {
        BadgeType.WeekTop3 => (LeaderboardPeriodType.Week, 3),
        BadgeType.MonthTop10 => (LeaderboardPeriodType.Month, 10),
        _ => throw new ArgumentOutOfRangeException(nameof(badge), badge, null),
    };

    public static BadgeAward Award(
        ScoreSubjectType subjectType, Guid subjectId, BadgeType badge, string periodKey, int rank, int points, DateTimeOffset at) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            SubjectType = subjectType,
            SubjectId = Guard.NotEmpty(subjectId),
            Badge = badge,
            PeriodKey = Guard.NotNullOrWhiteSpace(periodKey),
            Rank = Guard.InRange(rank, 1, Rule(badge).Places),
            Points = points,
            AwardedAt = Guard.Utc(at),
        };
}
