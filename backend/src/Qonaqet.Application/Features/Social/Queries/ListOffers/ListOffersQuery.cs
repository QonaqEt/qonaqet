using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Social.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Categories;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Social.Queries.ListOffers;

/// <summary>
/// <c>GET offers?categoryId=&amp;businessId=</c> (anonim): aktiv biznes və aktiv kateqoriyaların aktiv təklifləri, bu gün qalan
/// sayla. Kateqoriya ekranları bu siyahını <c>categoryId</c> ilə süzür; heç bir kateqoriyaya xüsusi məntiq yoxdur.
/// </summary>
[PublicRequest]
public sealed record ListOffersQuery(CategoryId? CategoryId, BusinessId? BusinessId) : IQuery<IReadOnlyList<PublicOfferDto>>;

public sealed class ListOffersHandler(IOfferRepository offers, IBusinessRepository businesses, ICategoryRepository categories, IClock clock)
    : IQueryHandler<ListOffersQuery, IReadOnlyList<PublicOfferDto>>
{
    public const int Limit = 200;

    public async Task<Result<IReadOnlyList<PublicOfferDto>>> Handle(ListOffersQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var list = await offers.ListActiveAsync(request.CategoryId, request.BusinessId, Limit, cancellationToken).ConfigureAwait(false);
        var activeCategories = (await categories.ListActiveAsync(cancellationToken).ConfigureAwait(false)).Select(c => c.Id).ToHashSet();

        var result = new List<PublicOfferDto>();
        var businessCache = new Dictionary<BusinessId, Business?>();
        var today = clock.BakuToday();
        foreach (var offer in list.Where(o => activeCategories.Contains(o.CategoryId)))
        {
            if (!businessCache.TryGetValue(offer.BusinessId, out var business))
            {
                business = await businesses.GetAsync(offer.BusinessId, cancellationToken).ConfigureAwait(false);
                businessCache[offer.BusinessId] = business;
            }

            if (business is { IsActive: true })
            {
                result.Add(offer.ToPublicDto(business, today));
            }
        }

        return Result<IReadOnlyList<PublicOfferDto>>.Success(result);
    }
}
