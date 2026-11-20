using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Social;

namespace Qonaqet.Application.Abstractions.Persistence;

/// <summary><c>offers</c>: sosial qonaqlıq təklifləri.</summary>
public interface IOfferRepository : IRepository<Offer, OfferId>
{
    /// <summary>Biznesin bütün (silinməmiş) təklifləri, ən yenilər əvvəl.</summary>
    Task<IReadOnlyList<Offer>> ListForBusinessAsync(BusinessId businessId, CancellationToken cancellationToken);

    /// <summary>Aktiv təkliflər (ictimai siyahı), filtrlərlə; ən yenilər əvvəl, ən çox <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<Offer>> ListActiveAsync(CategoryId? categoryId, BusinessId? businessId, int limit, CancellationToken cancellationToken);
}
