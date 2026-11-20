using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Campaigns.Common;
using Qonaqet.Domain.Social;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Campaigns.Queries;

/// <summary><c>GET admin/campaigns?status=</c>: bütün kampaniyalar (qaralamalar və seed şablonları daxil).</summary>
[RequiresRole(RoleNames.Admin)]
public sealed record ListCampaignsQuery(CampaignStatus? Status) : IQuery<IReadOnlyList<CampaignDto>>;

public sealed class ListCampaignsHandler(ICampaignRepository campaigns) : IQueryHandler<ListCampaignsQuery, IReadOnlyList<CampaignDto>>
{
    public async Task<Result<IReadOnlyList<CampaignDto>>> Handle(ListCampaignsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var list = await campaigns.ListAsync(request.Status, 200, cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<CampaignDto>>.Success([.. list.Select(c => c.ToDto())]);
    }
}
