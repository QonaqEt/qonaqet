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
/// qazanana ikinci dəfə xal vermir. Hadisədə nə verildiyi saxlanılmır — yalnız sayılar; <see cref="RecipientUserId"/> yalnız
/// göndərən→alıcı cüt limiti üçündür və heç vaxt lövhədə göstərilmir.
/// </summary>
public sealed class ScoreEvent : IAppendOnly
{
    /// <summary>Geri alınan mənbənin hadisəsi bu tiplə (<c>{SourceType}Reversal</c>) və eyni <see cref="SourceId"/> ilə yazılır.</summary>
    public const string ReversalSuffix = "Reversal";

    private ScoreEvent(
        ScoreSubjectType subjectType,
        Guid subjectId,
        string sourceType,
        Guid sourceId,
        int points,
        int treats,
        long value,
        Guid? recipientUserId,
        DateTimeOffset createdAt)
    {
        Id = Guid.CreateVersion7();
        SubjectType = subjectType;
        SubjectId = Guard.NotEmpty(subjectId);
        SourceType = Guard.NotNullOrWhiteSpace(sourceType);
        SourceId = Guard.NotEmpty(sourceId);
        Points = points;
        Treats = treats;
        Value = value;
        RecipientUserId = recipientUserId;
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

    /// <summary>Şəxsi hədiyyədə alıcı hesabı (cüt limiti üçün); başqa hadisələrdə boşdur.</summary>
    public Guid? RecipientUserId { get; private set; }

    public bool IsReversal => SourceType.EndsWith(ReversalSuffix, StringComparison.Ordinal);

    public DateTimeOffset CreatedAt { get; private set; }

    public static ScoreEvent Award(
        ScoreSubjectType subjectType,
        Guid subjectId,
        string sourceType,
        Guid sourceId,
        int points,
        int treats,
        long value,
        DateTimeOffset at,
        Guid? recipientUserId = null) =>
        new(subjectType, subjectId, sourceType, sourceId, Guard.InRange(points, 0, int.MaxValue), treats,
            value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Dəyər mənfi ola bilməz."), recipientUserId, at);

    /// <summary>
    /// Refund və ya geri alınan təhvil üçün düzəliş: əsl hadisənin xal, qonaqlıq və dəyərini mənfi ilə yazır (silinmir, append-only).
    /// Açar <c>{SourceType}Reversal</c> olduğundan eyni mənbə yalnız bir dəfə geri alınır. Anı əsl hadisəninkidir: düzəliş həmin günün,
    /// həftənin və ayın cəmindən çıxılır, ona görə <c>score_events</c>-dən yenidən hesablama artımlı aqreqatlarla eyni nəticə verir.
    /// </summary>
    public static ScoreEvent Reverse(ScoreEvent original)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (original.IsReversal)
        {
            throw new InvalidOperationException("Düzəliş hadisəsi yenidən geri alına bilməz.");
        }

        return new ScoreEvent(
            original.SubjectType,
            original.SubjectId,
            original.SourceType + ReversalSuffix,
            original.SourceId,
            -original.Points,
            -original.Treats,
            -original.Value,
            original.RecipientUserId,
            original.CreatedAt);
    }
}
