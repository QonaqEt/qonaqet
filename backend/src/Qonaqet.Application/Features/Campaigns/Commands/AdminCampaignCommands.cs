using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Campaigns.Common;
using Qonaqet.Domain.Social;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Campaigns.Commands;

/// <summary><c>POST admin/campaigns</c>: qaralama kampaniya (hovuz aktivləşdiriləndə yaranır).</summary>
[RequiresRole(RoleNames.Admin)]
public sealed record CreateCampaignCommand(CampaignInput Input) : ICommand<CampaignDto>;

public sealed class CreateCampaignHandler(ICampaignRepository campaigns, ICategoryRepository categories, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<CreateCampaignCommand, CampaignDto>
{
    public async Task<Result<CampaignDto>> Handle(CreateCampaignCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var valid = CampaignInputRules.Validate(request.Input);
        if (valid.IsFailure)
        {
            return valid.Error;
        }

        var references = await CampaignInputRules.CheckReferencesAsync(request.Input, null, categories, campaigns, cancellationToken).ConfigureAwait(false);
        if (references.IsFailure)
        {
            return references.Error;
        }

        var campaign = Campaign.CreateDraft(request.Input.ToDetails(), request.Input.CorporateId, clock.UtcNow, currentUser.ActorId);
        await campaigns.AddAsync(campaign, cancellationToken).ConfigureAwait(false);
        return campaign.ToDto();
    }
}

/// <summary><c>PUT admin/campaigns/{id}</c>: qaralama və ya aktiv kampaniyanın sahələri (aktivdə kateqoriya dəyişmir, bitmiş dəyişmir).</summary>
[RequiresRole(RoleNames.Admin)]
public sealed record UpdateCampaignCommand(CampaignId CampaignId, CampaignInput Input) : ICommand<CampaignDto>;

public sealed class UpdateCampaignHandler(ICampaignRepository campaigns, ICategoryRepository categories, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<UpdateCampaignCommand, CampaignDto>
{
    public async Task<Result<CampaignDto>> Handle(UpdateCampaignCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var campaign = await campaigns.GetAsync(request.CampaignId, cancellationToken).ConfigureAwait(false);
        if (campaign is null)
        {
            return DonationErrors.CampaignNotFound;
        }

        var valid = CampaignInputRules.Validate(request.Input);
        if (valid.IsFailure)
        {
            return valid.Error;
        }

        var references = await CampaignInputRules.CheckReferencesAsync(request.Input, campaign.Id, categories, campaigns, cancellationToken).ConfigureAwait(false);
        if (references.IsFailure)
        {
            return references.Error;
        }

        var details = request.Input.ToDetails();
        if (campaign.Status == CampaignStatus.Ended || (campaign.Status == CampaignStatus.Active && details.CategoryId != campaign.CategoryId))
        {
            return DonationErrors.CampaignNotEditable;
        }

        campaign.Update(details, clock.UtcNow, currentUser.ActorId);
        await campaigns.UpdateAsync(campaign, cancellationToken).ConfigureAwait(false);
        return campaign.ToDto();
    }
}

/// <summary><c>POST admin/campaigns/{id}/activate</c>: <c>Draft</c> → <c>Active</c>; eyni tranzaksiyada kampaniyanın hovuzu yaranır.</summary>
[RequiresRole(RoleNames.Admin)]
public sealed record ActivateCampaignCommand(CampaignId CampaignId) : ICommand<CampaignDto>;

public sealed class ActivateCampaignHandler(ICampaignRepository campaigns, ICategoryRepository categories, IPoolRepository pools, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<ActivateCampaignCommand, CampaignDto>
{
    public async Task<Result<CampaignDto>> Handle(ActivateCampaignCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var campaign = await campaigns.GetAsync(request.CampaignId, cancellationToken).ConfigureAwait(false);
        if (campaign is null)
        {
            return DonationErrors.CampaignNotFound;
        }

        var now = clock.UtcNow;
        if (campaign.Status != CampaignStatus.Draft || campaign.EndsAt <= now)
        {
            return DonationErrors.CampaignNotEditable;
        }

        if (await categories.GetAsync(campaign.CategoryId, cancellationToken).ConfigureAwait(false) is not { IsActive: true })
        {
            return SocialErrors.CategoryInactive;
        }

        var pool = Pool.CreateForCampaign(campaign.CategoryId, campaign.Id.Value, now, currentUser.ActorId);
        await pools.AddAsync(pool, cancellationToken).ConfigureAwait(false);
        campaign.Activate(pool.Id, now, currentUser.ActorId);
        await campaigns.UpdateAsync(campaign, cancellationToken).ConfigureAwait(false);
        return campaign.ToDto();
    }
}
