using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Domain.Leaderboard;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Admin.Commands.RecomputeLeaderboard;

/// <summary>
/// <c>POST admin/leaderboard/recompute</c>: admin bir lövhə dövrünü <c>score_events</c>-dən yenidən hesablayır (həqiqət mənbəyi hadisələrdir).
/// Bir Postgres tranzaksiyasında: sətirlər kilidlənir, yalnız fərqli olanlar düzəlir, əməliyyat audit log-a yazılır. Nəticə əvvəlki ilə eynidirsə
/// heç bir sətir dəyişmir (<c>changed = 0</c>). Dəyişiklik olubsa lövhə keşdən çıxarılır, növbəti oxu Postgres-dən qurur.
/// </summary>
[RequiresRole(RoleNames.Admin)]
public sealed record RecomputeLeaderboardCommand(ScoreSubjectType SubjectType, LeaderboardPeriodType PeriodType, string PeriodKey)
    : ICommand<LeaderboardRecompute>, IPostgresCommand;

public sealed class RecomputeLeaderboardValidator : Validator<RecomputeLeaderboardCommand>
{
    public RecomputeLeaderboardValidator() =>
        Rule("periodKey", x => LeaderboardPeriod.IsValidKey(x.PeriodType, x.PeriodKey), "period_key_invalid", "Dövr açarı düzgün deyil.");
}

public sealed class RecomputeLeaderboardHandler(ILeaderboardRepository entries, ILeaderboardStore store, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<RecomputeLeaderboardCommand, LeaderboardRecompute>
{
    public async Task<Result<LeaderboardRecompute>> Handle(RecomputeLeaderboardCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var board = new LeaderboardBoard(request.SubjectType, request.PeriodType, request.PeriodKey);
        var result = await entries.RecomputeAsync(board, currentUser.ActorId, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        if (result.Changed > 0)
        {
            await store.InvalidateAsync(board, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }
}
