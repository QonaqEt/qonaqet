using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Leaderboard.Common;
using Qonaqet.Domain.Leaderboard;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Leaderboard.Queries.GetLeaderboard;

/// <summary>
/// <c>GET leaderboard?subjectType=&amp;periodType=&amp;periodKey=</c>: İnsanlar · Bizneslər · Şirkətlər, həftəlik / aylıq / bütün zamanlar
/// (<c>periodKey</c> verilməsə cari Bakı dövrü). İlk 100 yer + "Sizin yeriniz" (<see cref="LeaderboardDto.Me"/>): istifadəçi lövhəsində
/// özü, biznes lövhəsində bağlı biznesi, şirkət lövhəsində şirkəti.
/// <para>
/// Məxfilik: yalnız ad və xal qaytarılır — nə verildiyi, kimə verildiyi, kateqoriya və ya istifadəçi ID-si heç vaxt. Görünməyənlər
/// (opt-out, bloklanmış və ya silinmiş istifadəçi, aktiv olmayan biznes) çıxarılır və yerlər ardıcıl yenidən sayılır.
/// </para>
/// </summary>
public sealed record GetLeaderboardQuery(ScoreSubjectType SubjectType, LeaderboardPeriodType PeriodType, string? PeriodKey) : IQuery<LeaderboardDto>;

public sealed record LeaderboardDto(
    ScoreSubjectType SubjectType, LeaderboardPeriodType PeriodType, string PeriodKey, IReadOnlyList<LeaderboardItemDto> Items, LeaderboardMeDto? Me);

/// <summary>Lövhə sətri; <see cref="Slug"/> yalnız biznes üçün (biznes səhifəsinə keçid).</summary>
public sealed record LeaderboardItemDto(int Rank, string DisplayName, int Points, string? Slug);

/// <summary>"Sizin yeriniz: #N". Pəncərədən kənardadırsa yer gizlənənlər daxil təxminidir.</summary>
public sealed record LeaderboardMeDto(int Rank, int Points);

public sealed class GetLeaderboardValidator : Validator<GetLeaderboardQuery>
{
    public GetLeaderboardValidator() =>
        Rule("periodKey", x => x.PeriodKey is null || LeaderboardPeriod.IsValidKey(x.PeriodType, x.PeriodKey), "period_key_invalid", "Dövr açarı düzgün deyil.");
}

public sealed class GetLeaderboardHandler(
    LeaderboardReader reader, ILeaderboardSubjects subjects, IUserRepository users, ICurrentUser currentUser, IClock clock)
    : IQueryHandler<GetLeaderboardQuery, LeaderboardDto>
{
    public const int Shown = 100;

    /// <summary>Gizlənənləri çıxdıqdan sonra da 100 yer qalsın deyə oxunan pəncərə.</summary>
    public const int Window = 200;

    public async Task<Result<LeaderboardDto>> Handle(GetLeaderboardQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var periodKey = request.PeriodKey ?? LeaderboardPeriod.Current(request.PeriodType, clock.UtcNow).Key;
        var board = new LeaderboardBoard(request.SubjectType, request.PeriodType, periodKey);
        var me = await MySubjectAsync(request.SubjectType, cancellationToken).ConfigureAwait(false);

        var snapshot = await reader.ReadAsync(board, Window, me, cancellationToken).ConfigureAwait(false);
        var visible = await subjects.VisibleAsync(request.SubjectType, [.. snapshot.Top.Select(s => s.SubjectId)], cancellationToken).ConfigureAwait(false);

        var items = new List<LeaderboardItemDto>();
        int? myVisibleRank = null;
        var rank = 0;
        foreach (var score in snapshot.Top)
        {
            var isMe = score.SubjectId == me;
            if (visible.TryGetValue(score.SubjectId, out var subject))
            {
                rank++;
                if (rank <= Shown)
                {
                    items.Add(new LeaderboardItemDto(rank, subject.DisplayName, score.Points, subject.Slug));
                }
            }

            if (isMe)
            {
                // Opt-out edən özü lövhədə görünmür, amma öz yerini görür: görünənlərin arasında harada olardı.
                myVisibleRank = visible.ContainsKey(score.SubjectId) ? rank : rank + 1;
            }
        }

        var mine = snapshot is { Rank: { } rawRank, Points: { } points } ? new LeaderboardMeDto(myVisibleRank ?? rawRank, points) : null;
        return new LeaderboardDto(request.SubjectType, request.PeriodType, periodKey, items, mine);
    }

    private async Task<Guid?> MySubjectAsync(ScoreSubjectType subjectType, CancellationToken cancellationToken)
    {
        if (currentUser.ActorId is not { } actorId)
        {
            return null;
        }

        if (subjectType == ScoreSubjectType.User)
        {
            return actorId;
        }

        var user = await users.GetAsync(UserId.From(actorId), cancellationToken).ConfigureAwait(false);
        return subjectType == ScoreSubjectType.Business ? user?.BusinessId : user?.CorporateId;
    }
}
