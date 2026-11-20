using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Donations.Commands.DonatePool;
using Qonaqet.Application.Features.Donations.Common;
using Qonaqet.Domain.Social;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Campaigns.Commands;

/// <summary>
/// <c>POST donations/campaign</c>: açıq kampaniyanın hovuzuna ianə (1–1000 AZN, adlı/anonim). Təsdiqdən sonra kampaniya hovuzunun balansı və
/// kampaniyanın <c>raisedQepik</c>-i bir tranzaksiyada artır, ledger-ə <c>PoolDeposit</c> yazılır. Geri qaytarılmır.
/// </summary>
[RequiresActiveAccount]
public sealed record DonateCampaignCommand(CampaignId CampaignId, long AmountQepik, DonorDisplay DonorDisplay, string? DonorName)
    : ICommand<DonationStartedDto>, INonTransactionalCommand, IIdempotentCommand;

public sealed class DonateCampaignValidator : Validator<DonateCampaignCommand>
{
    public DonateCampaignValidator()
    {
        Rule("amountQepik", x => x.AmountQepik is >= DonationRules.MinAmountQepik and <= DonationRules.MaxAmountQepik,
            DonationErrors.AmountOutOfRange.Code, DonationErrors.AmountOutOfRange.Message);
        Rule("donorDisplay", x => Enum.IsDefined(x.DonorDisplay), "invalid_donor_display", "Göstərim düzgün deyil.");
        RuleFor<string?>(x => x.DonorName).MaxLength(DonationRules.DonorNameMaxLength);
    }
}

public sealed class DonateCampaignHandler(
    ICampaignRepository campaigns, IUserRepository users, PoolDonationStarter starter, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<DonateCampaignCommand, DonationStartedDto>
{
    public async Task<Result<DonationStartedDto>> Handle(DonateCampaignCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        var user = await users.GetAsync(UserId.From(userId), cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return CommonErrors.Unauthorized;
        }

        var campaign = await campaigns.GetAsync(request.CampaignId, cancellationToken).ConfigureAwait(false);
        if (campaign is null)
        {
            return DonationErrors.CampaignNotFound;
        }

        if (!campaign.IsOpenAt(clock.UtcNow) || campaign.PoolId is not { } poolId)
        {
            return DonationErrors.CampaignNotActive;
        }

        return await starter.StartAsync(poolId, campaign.Id.Value, request.AmountQepik, request.DonorDisplay, request.DonorName, user, cancellationToken)
            .ConfigureAwait(false);
    }
}
