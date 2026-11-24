using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Leaderboard;

/// <summary>Reytinqdə xal qazanan: insanlar, bizneslər, şirkətlər (üç ayrı lövhə).</summary>
public enum ScoreSubjectType
{
    User,
    Business,
    Corporate,
}

/// <summary>
/// Xal hadisəsi (Postgres <c>score_events</c>, append-only). Xal yalnız <b>təhvil</b> anında yazılır; düzəliş mənfi hadisə ilədir.
/// (<see cref="SubjectType"/>, <see cref="SubjectId"/>, <see cref="SourceType"/>, <see cref="SourceId"/>) unikaldır: eyni mənbə eyni
/// qazanana ikinci dəfə xal vermir. Hadisədə nə verildiyi və kimə verildiyi saxlanılmır — yalnız sayılar.
/// </summary>
public sealed class ScoreEvent : IAppendOnly
{
    private ScoreEvent(
        ScoreSubjectType subjectType, Guid subjectId, string sourceType, Guid sourceId, int points, int treats, long value, DateTimeOffset createdAt)
    {
        Id = Guid.CreateVersion7();
        SubjectType = subjectType;
        SubjectId = Guard.NotEmpty(subjectId);
        SourceType = Guard.NotNullOrWhiteSpace(sourceType);
        SourceId = Guard.NotEmpty(sourceId);
        Points = points;
        Treats = treats;
        Value = value;
        CreatedAt = Guard.Utc(createdAt);
    }

    private ScoreEvent()
    {
    }

    public Guid Id { get; private set; }

    public ScoreSubjectType SubjectType { get; private set; }

    public Guid SubjectId { get; private set; }

    /// <summary>Mənbənin tipi (<c>GiftRedemption</c>, <c>Gift</c>, <c>Claim</c>).</summary>
    public string SourceType { get; private set; } = string.Empty;

    public Guid SourceId { get; private set; }

    public int Points { get; private set; }

    /// <summary>Qonaq edilən insan sayı (bir təhvil = 1).</summary>
    public int Treats { get; private set; }

    /// <summary>Təhvil olunan dəyər, qəpiklə (pulsuz təkliflərdə 0).</summary>
    public long Value { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static ScoreEvent Award(
        ScoreSubjectType subjectType, Guid subjectId, string sourceType, Guid sourceId, int points, int treats, long value, DateTimeOffset at) =>
        new(subjectType, subjectId, sourceType, sourceId, Guard.InRange(points, 0, int.MaxValue), treats,
            value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Dəyər mənfi ola bilməz."), at);
}
