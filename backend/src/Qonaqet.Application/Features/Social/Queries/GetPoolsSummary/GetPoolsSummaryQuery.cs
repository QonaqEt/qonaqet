using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Social.Queries.GetPoolsSummary;

/// <summary>
/// <c>GET pools/summary</c> (anonim): aktiv kateqoriyalar üzrə hovuz aqreqatları — mövcud məbləğ, toplanmış ianə, maliyyələşən qonaqlıqlar.
/// Donor və ya alıcı siyahısı yoxdur.
/// </summary>
[PublicRequest]
public sealed record GetPoolsSummaryQuery : IQuery<IReadOnlyList<PoolSummaryDto>>;

public sealed record PoolSummaryDto(Guid CategoryId, string CategoryName, long AvailableQepik, long DonatedQepik, long TreatsFunded);

public sealed class GetPoolsSummaryHandler(ISocialStatsReader stats, ICategoryRepository categories) : IQueryHandler<GetPoolsSummaryQuery, IReadOnlyList<PoolSummaryDto>>
{
    public async Task<Result<IReadOnlyList<PoolSummaryDto>>> Handle(GetPoolsSummaryQuery request, CancellationToken cancellationToken)
    {
        var active = (await categories.ListActiveAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id.Value);
        var rows = await stats.PoolSummaryAsync(cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<PoolSummaryDto>>.Success([.. rows
            .Where(r => active.ContainsKey(r.CategoryId))
            .OrderBy(r => active[r.CategoryId].SortOrder)
            .Select(r => new PoolSummaryDto(r.CategoryId, active[r.CategoryId].Name, r.AvailableQepik, r.DonatedQepik, r.TreatsFunded))]);
    }
}
