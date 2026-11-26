using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Common;
using Qonaqet.Domain.Businesses;

namespace Qonaqet.Infrastructure.Persistence.Mongo.Repositories;

/// <summary>
/// İctimai biznes axtarışı. Koordinatla: <c>$geoNear</c> (<c>location</c> 2dsphere partial indeksi; sorğu <c>isDeleted: false</c>
/// şərtini literal daşıyır ki, partial indeks seçilsin) → məsafəyə görə sıralı, <c>$facet</c> ilə səhifə və cəm bir sorğuda.
/// Koordinatsız: (<c>status</c>, …) partial indeksləri ilə filtr, ad üzrə sıralama.
/// </summary>
public sealed class MongoBusinessSearch(MongoContext context) : IBusinessSearch
{
    private const double EarthMetersPerKm = 1000;

    public async Task<PagedResult<BusinessSearchHit>> SearchAsync(BusinessSearchCriteria criteria, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        var collection = context.Collection<Business>(BusinessCollectionNames.Businesses);
        var filter = Filter(criteria);

        if (criteria is not { Latitude: { } lat, Longitude: { } lng })
        {
            var find = collection.Find(filter);
            var total = await find.CountDocumentsAsync(cancellationToken).ConfigureAwait(false);
            var items = await find.SortBy(b => b.Name).ThenBy(b => b.Id).Skip(page.Skip).Limit(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
            return new PagedResult<BusinessSearchHit>([.. items.Select(b => new BusinessSearchHit(b, null))], total);
        }

        var geoNear = new BsonDocument("$geoNear", new BsonDocument
        {
            { "near", new BsonDocument { { "type", "Point" }, { "coordinates", new BsonArray { lng, lat } } } },
            { "distanceField", "distanceMeters" },
            { "maxDistance", criteria.RadiusKm * EarthMetersPerKm },
            { "spherical", true },
            { "key", "location" },
            { "query", filter },
        });
        var facet = new BsonDocument("$facet", new BsonDocument
        {
            { "total", new BsonArray { new BsonDocument("$count", "n") } },
            { "items", new BsonArray { new BsonDocument("$skip", page.Skip), new BsonDocument("$limit", page.PageSize) } },
        });

        var pipeline = PipelineDefinition<Business, BsonDocument>.Create(new[] { geoNear, facet });
        var result = await (await collection.AggregateAsync(pipeline, cancellationToken: cancellationToken).ConfigureAwait(false))
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return new PagedResult<BusinessSearchHit>([], 0);
        }

        var totalCount = result["total"].AsBsonArray is { Count: > 0 } t ? t[0]["n"].ToInt64() : 0;
        var hits = result["items"].AsBsonArray
            .Select(d => d.AsBsonDocument)
            .Select(d => new BusinessSearchHit(BsonSerializer.Deserialize<Business>(d), d["distanceMeters"].ToDouble()))
            .ToList();
        return new PagedResult<BusinessSearchHit>(hits, totalCount);
    }

    /// <summary>Yalnız silinməmiş və aktiv bizneslər; bərabərlik sahələri partial indekslərin sırası ilə.</summary>
    private static BsonDocument Filter(BusinessSearchCriteria criteria)
    {
        var filter = new BsonDocument
        {
            { "isDeleted", false }, { "status", nameof(BusinessStatus.Active) }, { "isSandbox", new BsonDocument("$ne", true) },
        };
        if (criteria.AcceptsPool is { } pool)
        {
            filter.Add("acceptsPool", pool);
        }

        if (criteria.AcceptsNetworkVouchers is { } network)
        {
            filter.Add("acceptsNetworkVouchers", network);
        }

        if (criteria.Type is { } type)
        {
            filter.Add("type", type.ToString());
        }

        return filter;
    }
}
