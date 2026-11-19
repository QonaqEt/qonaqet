using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Businesses.Common;
using Qonaqet.Application.Features.Catalog.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Catalog.Queries.ListCatalogItems;

/// <summary><c>GET business/{businessId}/catalog</c>: üzvlər üçün bütün elementlər (aktiv olmayanlar da), qrup və başlıq üzrə.</summary>
public sealed record ListCatalogItemsQuery(BusinessId BusinessId) : IQuery<IReadOnlyList<CatalogItemDto>>;

public sealed class ListCatalogItemsHandler(BusinessAccess access, ICatalogItemRepository items) : IQueryHandler<ListCatalogItemsQuery, IReadOnlyList<CatalogItemDto>>
{
    public async Task<Result<IReadOnlyList<CatalogItemDto>>> Handle(ListCatalogItemsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var grant = await access.RequireAsync(request.BusinessId, BusinessPermission.Operate, cancellationToken).ConfigureAwait(false);
        if (grant.IsFailure)
        {
            return grant.Error;
        }

        var list = await items.ListForBusinessAsync(request.BusinessId, activeOnly: false, cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<CatalogItemDto>>.Success([.. list.Select(i => i.ToDto())]);
    }
}
