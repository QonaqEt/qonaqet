using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Domain.Leaderboard;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Admin.Queries.GetScorePoints;

/// <summary><c>GET admin/leaderboard/points</c>: hazırda qüvvədə olan xal dəyərləri; <see cref="ScorePointsDto.ChangedAt"/> <c>null</c>-dırsa konfiqurasiya default-udur.</summary>
[RequiresRole(RoleNames.Admin)]
public sealed record GetScorePointsQuery : IQuery<ScorePointsDto>;

public sealed record ScorePointsDto(
    int GiftItemUnit,
    int GiftVoucherStep,
    long GiftVoucherStepQepik,
    int DonorClaim,
    int BusinessClaim,
    int CorporateGift,
    int CorporateClaim,
    DateTimeOffset? ChangedAt);

public static class ScorePointsMapping
{
    public static ScorePointsDto ToDto(this ScorePointsVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        var p = version.Points;
        return new ScorePointsDto(
            p.GiftItemUnit, p.GiftVoucherStep, p.GiftVoucherStepQepik, p.DonorClaim, p.BusinessClaim, p.CorporateGift, p.CorporateClaim, version.ChangedAt);
    }
}

public sealed class GetScorePointsHandler(IScorePointSettings settings, IClock clock) : IQueryHandler<GetScorePointsQuery, ScorePointsDto>
{
    public async Task<Result<ScorePointsDto>> Handle(GetScorePointsQuery request, CancellationToken cancellationToken) =>
        (await settings.AtAsync(clock.UtcNow, cancellationToken).ConfigureAwait(false)).ToDto();
}
