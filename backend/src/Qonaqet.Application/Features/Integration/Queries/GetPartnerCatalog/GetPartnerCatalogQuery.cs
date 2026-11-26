using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Integration.Common;
using Qonaqet.Domain.Integration;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Integration.Queries.GetPartnerCatalog;

/// <summary><c>GET /partner/v1/catalog/{businessId}</c> (<c>catalog:write</c>): biznesin cari kataloqu (aktiv və deaktiv, silinməmiş).</summary>
[RequiresRole(PartnerScopes.CatalogWrite)]
public sealed record GetPartnerCatalogQuery(Guid BusinessId) : IQuery<IReadOnlyList<PartnerCatalogItemDto>>;

public sealed record PartnerCatalogItemDto(
    Guid Id, string? ExternalId, string Title, string? Description, long PriceQepik, string? Group, bool IsActive, bool IsGiftable);

public sealed class GetPartnerCatalogHandler(PartnerAccess access, ICatalogItemRepository catalog)
    : IQueryHandler<GetPartnerCatalogQuery, IReadOnlyList<PartnerCatalogItemDto>>
{
    public async Task<Result<IReadOnlyList<PartnerCatalogItemDto>>> Handle(GetPartnerCatalogQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var granted = await access.RequireBusinessAsync(request.BusinessId, cancellationToken).ConfigureAwait(false);
        if (granted.IsFailure)
        {
            return granted.Error;
        }

        var items = await catalog.ListForBusinessAsync(granted.Value.Business.Id, activeOnly: false, cancellationToken).ConfigureAwait(false);
        return items.Select(i => new PartnerCatalogItemDto(i.Id.Value, i.ExternalId, i.Title, i.Description, i.PriceQepik, i.Group, i.IsActive, i.IsGiftable))
            .ToList();
    }
}
