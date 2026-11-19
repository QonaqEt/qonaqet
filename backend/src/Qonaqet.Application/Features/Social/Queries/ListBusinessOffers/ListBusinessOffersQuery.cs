using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Businesses.Common;
using Qonaqet.Application.Features.Social.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Social.Queries.ListBusinessOffers;

/// <summary><c>GET business/{businessId}/offers</c>: biznesin təklifləri (üzvlər üçün), yalnız saylarla — alıcı məlumatı yoxdur.</summary>
public sealed record ListBusinessOffersQuery(BusinessId BusinessId) : IQuery<IReadOnlyList<OfferDto>>;

public sealed class ListBusinessOffersHandler(BusinessAccess access, IOfferRepository offers, ICategoryRepository categories, IClock clock)
    : IQueryHandler<ListBusinessOffersQuery, IReadOnlyList<OfferDto>>
{
    public async Task<Result<IReadOnlyList<OfferDto>>> Handle(ListBusinessOffersQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var grant = await access.RequireAsync(request.BusinessId, BusinessPermission.Operate, cancellationToken).ConfigureAwait(false);
        if (grant.IsFailure)
        {
            return grant.Error;
        }

        var list = await offers.ListForBusinessAsync(request.BusinessId, cancellationToken).ConfigureAwait(false);
        var byId = (await categories.ListActiveAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id);
        var today = clock.BakuToday();
        return Result<IReadOnlyList<OfferDto>>.Success([.. list.Select(o => o.ToDto(byId.GetValueOrDefault(o.CategoryId), today))]);
    }
}
