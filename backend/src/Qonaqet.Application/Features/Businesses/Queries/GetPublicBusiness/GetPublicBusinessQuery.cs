using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Businesses.Common;
using Qonaqet.Application.Features.Catalog.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Businesses.Queries.GetPublicBusiness;

/// <summary>
/// <c>GET businesses/{idOrSlug}</c> (anonim): aktiv biznesin ictimai səhifəsi və aktiv kataloqu. Aktiv olmayan
/// (gözləyən, dayandırılmış, silinmiş) biznes tapılmır.
/// </summary>
[PublicRequest]
public sealed record GetPublicBusinessQuery(string IdOrSlug) : IQuery<PublicBusinessPageDto>;

public sealed record PublicBusinessPageDto(PublicBusinessDto Business, IReadOnlyList<CatalogItemDto> Catalog);

public sealed class GetPublicBusinessHandler(IBusinessRepository businesses, ICatalogItemRepository catalog)
    : IQueryHandler<GetPublicBusinessQuery, PublicBusinessPageDto>
{
    public async Task<Result<PublicBusinessPageDto>> Handle(GetPublicBusinessQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var business = Guid.TryParse(request.IdOrSlug, out var id)
            ? await businesses.GetAsync(BusinessId.From(id), cancellationToken).ConfigureAwait(false)
            : await businesses.GetBySlugAsync(request.IdOrSlug.Trim().ToLowerInvariant(), cancellationToken).ConfigureAwait(false);
        if (business is not { IsActive: true })
        {
            return BusinessErrors.NotFound;
        }

        var items = await catalog.ListForBusinessAsync(business.Id, activeOnly: true, cancellationToken).ConfigureAwait(false);
        return new PublicBusinessPageDto(business.ToPublicDto(null), [.. items.Select(i => i.ToDto())]);
    }
}
