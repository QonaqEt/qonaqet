using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Common;
using Qonaqet.Application.Features.Businesses.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Businesses.Queries.SearchBusinesses;

/// <summary>
/// <c>GET businesses?lat=&amp;lng=&amp;radiusKm=&amp;type=&amp;acceptsPool=&amp;acceptsNetworkVouchers=&amp;page=&amp;pageSize=</c> (anonim).
/// Yalnız aktiv bizneslər. Koordinat verilibsə radius daxilində yaxından uzağa (default 5 km, ən çox 50 km), yoxsa ad üzrə.
/// Cavabda VÖEN, IBAN və sahib məlumatı yoxdur.
/// </summary>
[PublicRequest]
public sealed record SearchBusinessesQuery(
    double? Latitude,
    double? Longitude,
    double? RadiusKm,
    BusinessType? Type,
    bool? AcceptsPool,
    bool? AcceptsNetworkVouchers,
    int? Page,
    int? PageSize) : IQuery<PagedResult<PublicBusinessDto>>;

public sealed class SearchBusinessesValidator : Validator<SearchBusinessesQuery>
{
    public const double DefaultRadiusKm = 5;
    public const double MaxRadiusKm = 50;

    public SearchBusinessesValidator()
    {
        Rule("location", x => (x.Latitude is null) == (x.Longitude is null), "location_incomplete", "lat və lng birlikdə verilməlidir.");
        Rule("location", x => x.Latitude is null || x.Longitude is null || GeoPoint.IsValid(x.Latitude.Value, x.Longitude.Value), "location_invalid", "Koordinatlar düzgün deyil.");
        RuleFor(x => x.RadiusKm).Must(r => r is null || (double.IsFinite(r.Value) && r > 0 && r <= MaxRadiusKm), "radius_invalid", $"Radius 0 ilə {MaxRadiusKm} km arasında olmalıdır.");
        RuleFor(x => x.Type).Must(t => t is null || Enum.IsDefined(t.Value), "business_type_invalid", "Naməlum biznes növü.");
    }
}

public sealed class SearchBusinessesHandler(IBusinessSearch search) : IQueryHandler<SearchBusinessesQuery, PagedResult<PublicBusinessDto>>
{
    public async Task<Result<PagedResult<PublicBusinessDto>>> Handle(SearchBusinessesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var criteria = new BusinessSearchCriteria(
            request.Latitude, request.Longitude, request.RadiusKm ?? SearchBusinessesValidator.DefaultRadiusKm, request.Type, request.AcceptsPool, request.AcceptsNetworkVouchers);
        var page = await search.SearchAsync(criteria, PageRequest.Of(request.Page, request.PageSize), cancellationToken).ConfigureAwait(false);
        return new PagedResult<PublicBusinessDto>([.. page.Items.Select(h => h.Business.ToPublicDto(h.DistanceMeters))], page.Total);
    }
}
