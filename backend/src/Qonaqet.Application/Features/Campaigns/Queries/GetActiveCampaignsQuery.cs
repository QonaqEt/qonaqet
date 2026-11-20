using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Campaigns.Queries;

/// <summary><c>GET campaigns/active</c> (anonim): indi açıq kampaniyalar, ən tez bitən əvvəl, məqsəd faizi ilə (ana səhifə karuseli).</summary>
[PublicRequest]
public sealed record GetActiveCampaignsQuery : IQuery<IReadOnlyList<ActiveCampaignDto>>;

public sealed record ActiveCampaignDto(
    Guid Id, string Title, string Slug, string Description, Guid? BannerFileId, Guid CategoryId, DateTimeOffset EndsAt, long GoalQepik, long RaisedQepik,
    int ProgressPercent);

public sealed class GetActiveCampaignsHandler(ICampaignRepository campaigns, IClock clock) : IQueryHandler<GetActiveCampaignsQuery, IReadOnlyList<ActiveCampaignDto>>
{
    public async Task<Result<IReadOnlyList<ActiveCampaignDto>>> Handle(GetActiveCampaignsQuery request, CancellationToken cancellationToken)
    {
        var open = await campaigns.ListOpenAsync(clock.UtcNow, 50, cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<ActiveCampaignDto>>.Success([.. open.Select(c => new ActiveCampaignDto(
            c.Id.Value, c.Title, c.Slug, c.Description, c.BannerFileId, c.CategoryId.Value, c.EndsAt, c.GoalQepik, c.RaisedQepik, c.ProgressPercent))]);
    }
}
