using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Domain.Leaderboard;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Leaderboard.Queries.GetMyScore;

/// <summary>
/// <c>GET me/score</c>: istifadəçinin səviyyəsi (bütün zamanların xalı ilə), növbəti səviyyəyə qalan xal, bu həftə / bu ay / bütün zamanlar,
/// nişanları və son xal tarixçəsi. Tarixçə yalnız növ, xal və tarixdir — nə və kimə verildiyi göstərilmir.
/// </summary>
public sealed record GetMyScoreQuery : IQuery<MyScoreDto>;

public sealed record MyScoreDto(
    LevelDto Level, LevelDto? NextLevel, int PointsToNextLevel, ScorePeriodsDto Points, IReadOnlyList<BadgeDto> Badges, IReadOnlyList<ScoreHistoryItemDto> History);

public sealed record LevelDto(string Name, int MinPoints);

public sealed record ScorePeriodsDto(int Week, int Month, int AllTime);

public sealed record BadgeDto(BadgeType Badge, string PeriodKey, int Rank, DateTimeOffset AwardedAt);

/// <summary><see cref="Kind"/>: <c>Gift</c> (hədiyyə), <c>Treat</c> (sosial qonaqlıq), <c>Correction</c> (geri alınma).</summary>
public sealed record ScoreHistoryItemDto(string Kind, int Points, int Treats, DateTimeOffset At);

public sealed class GetMyScoreHandler(ILeaderboardRepository entries, ICurrentUser currentUser, IClock clock) : IQueryHandler<GetMyScoreQuery, MyScoreDto>
{
    public const int HistoryLimit = 20;

    public async Task<Result<MyScoreDto>> Handle(GetMyScoreQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        var periods = LeaderboardPeriod.For(clock.UtcNow);
        var points = await entries.PointsAsync(ScoreSubjectType.User, userId, periods, cancellationToken).ConfigureAwait(false);
        var byType = periods.ToDictionary(p => p.Type, p => points.GetValueOrDefault(p));
        var allTime = byType[LeaderboardPeriodType.AllTime];

        var level = Level.For(allTime);
        var next = Level.Next(allTime);
        var badges = await entries.BadgesAsync(ScoreSubjectType.User, userId, cancellationToken).ConfigureAwait(false);
        var history = await entries.HistoryAsync(ScoreSubjectType.User, userId, HistoryLimit, cancellationToken).ConfigureAwait(false);

        return new MyScoreDto(
            new LevelDto(level.Name, level.MinPoints),
            next is null ? null : new LevelDto(next.Name, next.MinPoints),
            next is null ? 0 : next.MinPoints - allTime,
            new ScorePeriodsDto(byType[LeaderboardPeriodType.Week], byType[LeaderboardPeriodType.Month], allTime),
            [.. badges.Select(b => new BadgeDto(b.Badge, b.PeriodKey, b.Rank, b.AwardedAt))],
            [.. history.Select(e => new ScoreHistoryItemDto(Kind(e), e.Points, e.Treats, e.CreatedAt))]);
    }

    private static string Kind(ScoreEvent scoreEvent) =>
        scoreEvent.IsReversal ? "Correction"
        : scoreEvent.SourceType == ScoringPolicy.ClaimSource ? "Treat"
        : "Gift";
}
