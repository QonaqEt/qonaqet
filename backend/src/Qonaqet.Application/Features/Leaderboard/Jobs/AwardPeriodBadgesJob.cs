using Qonaqet.Application.Abstractions.Jobs;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Domain.Leaderboard;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Leaderboard.Jobs;

/// <summary>
/// Dövr sonu nişanları (Worker, saatlıq yoxlama, tək icraçı): bitmiş son Bakı həftəsinin ilk 3-ü <c>Week Top 3</c>, bitmiş son ayın ilk 10-u
/// <c>Month Top 10</c> alır — hər üç lövhədə (insanlar, bizneslər, şirkətlər). Yerlər lövhədəki kimidir: xal çoxdan aza, yalnız görünən
/// (opt-in, bloklanmamış, aktiv) və xalı müsbət olanlar. İdempotentdir: dövr üçün nişan artıq verilibsə o lövhə atlanır, və
/// <c>badge_awards</c>-ın unikal açarı ikinci nişanı rədd edir.
/// </summary>
public sealed class AwardPeriodBadgesJob(ILeaderboardRepository entries, ILeaderboardSubjects subjects, IClock clock) : IScheduledJob
{
    public const string Name = "leaderboard-badges";

    /// <summary>Gizlənənlər çıxdıqdan sonra yerləri doldurmaq üçün baxılan namizəd sayı.</summary>
    private const int Candidates = 200;

    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var awarded = 0;
        foreach (var badge in Enum.GetValues<BadgeType>())
        {
            var (periodType, places) = BadgeAward.Rule(badge);
            var period = LeaderboardPeriod.Previous(periodType, now);
            foreach (var subjectType in Enum.GetValues<ScoreSubjectType>())
            {
                if (await entries.BadgesAwardedAsync(subjectType, badge, period.Key, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                var board = new LeaderboardBoard(subjectType, periodType, period.Key);
                var scores = await entries.ScoresAsync(board, cancellationToken).ConfigureAwait(false);
                var ranked = scores.Where(s => s.Points > 0)
                    .OrderByDescending(s => s.Points).ThenByDescending(s => s.SubjectId.ToString(), StringComparer.Ordinal)
                    .ToList();
                var visible = await subjects.VisibleAsync(subjectType, [.. ranked.Take(Candidates).Select(s => s.SubjectId)], cancellationToken).ConfigureAwait(false);
                var winners = ranked.Where(s => visible.ContainsKey(s.SubjectId)).Take(places)
                    .Select((s, i) => BadgeAward.Award(subjectType, s.SubjectId, badge, period.Key, i + 1, s.Points, now))
                    .ToList();
                if (winners.Count > 0)
                {
                    await entries.AddBadgesAsync(winners, cancellationToken).ConfigureAwait(false);
                    awarded += winners.Count;
                }
            }
        }

        return awarded;
    }
}
