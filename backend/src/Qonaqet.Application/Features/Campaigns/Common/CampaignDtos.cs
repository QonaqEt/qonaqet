using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Social;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Campaigns.Common;

/// <summary>Admin görünüşü.</summary>
public sealed record CampaignDto(
    Guid Id, string Title, string Slug, string Description, Guid? BannerFileId, Guid CategoryId, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    long GoalQepik, long RaisedQepik, long RolledOverQepik, int ProgressPercent, CampaignStatus Status, Guid? PoolId);

/// <summary>Admin formasının sahələri.</summary>
public sealed record CampaignInput(
    string Title, string Slug, string? Description, Guid? BannerFileId, Guid CategoryId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, long GoalQepik)
{
    public CampaignDetails ToDetails() =>
        new(Title, Slug, Description ?? string.Empty, BannerFileId, Domain.Categories.CategoryId.From(CategoryId), StartsAt.ToUniversalTime(), EndsAt.ToUniversalTime(), GoalQepik);
}

public static class CampaignMappings
{
    public static CampaignDto ToDto(this Campaign campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        return new CampaignDto(
            campaign.Id.Value, campaign.Title, campaign.Slug, campaign.Description, campaign.BannerFileId, campaign.CategoryId.Value, campaign.StartsAt,
            campaign.EndsAt, campaign.GoalQepik, campaign.RaisedQepik, campaign.RolledOverQepik, campaign.ProgressPercent, campaign.Status, campaign.PoolId?.Value);
    }
}

/// <summary>Formanın ortaq yoxlamaları (sahələr) — validatorlar bunu işlədir.</summary>
public static class CampaignInputRules
{
    public static Result Validate(CampaignInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var valid = !string.IsNullOrWhiteSpace(input.Title) && input.Title.Length <= Campaign.TitleMaxLength
            && (input.Description?.Length ?? 0) <= Campaign.DescriptionMaxLength
            && Campaign.IsValidSlug(input.Slug)
            && input.EndsAt > input.StartsAt
            && input.GoalQepik is > 0 and <= Campaign.MaxGoalQepik
            && input.CategoryId != Guid.Empty;
        return valid ? Result.Success() : DonationErrors.CampaignInvalid;
    }

    /// <summary>Kateqoriya mövcud və aktivdir, slug başqa kampaniyada işlənmir.</summary>
    public static async Task<Result> CheckReferencesAsync(
        CampaignInput input, CampaignId? self, ICategoryRepository categories, ICampaignRepository campaigns, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var category = await categories.GetAsync(CategoryId.From(input.CategoryId), cancellationToken).ConfigureAwait(false);
        if (category is not { IsActive: true })
        {
            return SocialErrors.CategoryInactive;
        }

        return await campaigns.SlugExistsAsync(input.Slug, self, cancellationToken).ConfigureAwait(false)
            ? DonationErrors.CampaignSlugTaken
            : Result.Success();
    }
}
