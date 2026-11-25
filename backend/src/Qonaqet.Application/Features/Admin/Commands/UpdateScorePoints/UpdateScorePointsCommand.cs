using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Admin.Queries.GetScorePoints;
using Qonaqet.Domain.Leaderboard;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Admin.Commands.UpdateScorePoints;

/// <summary>
/// <c>PUT admin/leaderboard/points</c>: admin xal dəyərlərini dəyişir. Yeni versiya yalnız bundan sonrakı təhvillərə tətbiq olunur; yazılmış
/// <c>score_events</c> və lövhələr dəyişmir (yenidən hesablama da hadisələrdəki xalı cəmləyir). Köhnə versiyalar tarixçə kimi qalır.
/// </summary>
[RequiresRole(RoleNames.Admin)]
public sealed record UpdateScorePointsCommand(
    int GiftItemUnit,
    int GiftVoucherStep,
    long GiftVoucherStepQepik,
    int DonorClaim,
    int BusinessClaim,
    int CorporateGift,
    int CorporateClaim) : ICommand<ScorePointsDto>, IPostgresCommand;

public sealed class UpdateScorePointsValidator : Validator<UpdateScorePointsCommand>
{
    public const int MaxPoints = 100;

    public const long MinStepQepik = 100;

    public const long MaxStepQepik = 100_00;

    public UpdateScorePointsValidator()
    {
        Points("giftItemUnit", x => x.GiftItemUnit);
        Points("giftVoucherStep", x => x.GiftVoucherStep);
        Points("donorClaim", x => x.DonorClaim);
        Points("businessClaim", x => x.BusinessClaim);
        Points("corporateGift", x => x.CorporateGift);
        Points("corporateClaim", x => x.CorporateClaim);
        Rule(
            "giftVoucherStepQepik",
            x => x.GiftVoucherStepQepik is >= MinStepQepik and <= MaxStepQepik,
            "voucher_step_out_of_range",
            "Vauçer addımı 1 ilə 100 AZN arasında olmalıdır.");
    }

    private void Points(string field, Func<UpdateScorePointsCommand, int> value) =>
        Rule(field, x => value(x) is >= 0 and <= MaxPoints, "points_out_of_range", $"Xal 0 ilə {MaxPoints} arasında olmalıdır.");
}

public sealed class UpdateScorePointsHandler(IScorePointSettings settings, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<UpdateScorePointsCommand, ScorePointsDto>
{
    public async Task<Result<ScorePointsDto>> Handle(UpdateScorePointsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var points = new ScorePoints
        {
            GiftItemUnit = request.GiftItemUnit,
            GiftVoucherStep = request.GiftVoucherStep,
            GiftVoucherStepQepik = request.GiftVoucherStepQepik,
            DonorClaim = request.DonorClaim,
            BusinessClaim = request.BusinessClaim,
            CorporateGift = request.CorporateGift,
            CorporateClaim = request.CorporateClaim,
        };
        return (await settings.SetAsync(points, currentUser.ActorId, clock.UtcNow, cancellationToken).ConfigureAwait(false)).ToDto();
    }
}
