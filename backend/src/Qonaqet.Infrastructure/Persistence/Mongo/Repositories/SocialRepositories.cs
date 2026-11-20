using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Social.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Social;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Persistence.Mongo.Repositories;

public sealed class OfferRepository(MongoContext context, IAggregateTracker tracker, IOutbox outbox, IClock clock)
    : SoftDeleteRepository<Offer, OfferId>(context, tracker, outbox, clock, SocialCollectionNames.Offers), IOfferRepository
{
    public async Task<IReadOnlyList<Offer>> ListForBusinessAsync(BusinessId businessId, CancellationToken cancellationToken) =>
        [.. (await FindAsync(Filter.Eq(x => x.BusinessId, businessId), cancellationToken).ConfigureAwait(false)).OrderByDescending(o => o.CreatedAt)];

    public async Task<IReadOnlyList<Offer>> ListActiveAsync(CategoryId? categoryId, BusinessId? businessId, int limit, CancellationToken cancellationToken)
    {
        // Gündəlik təklif "isActive" ilə, askıda isə qalığı ilə açıqdır; bitmiş askıda siyahıda görünmür.
        var filter = Filter.And(
            Filter.Eq(x => x.IsActive, true),
            Filter.Or(Filter.Eq(x => x.Type, OfferType.BusinessDaily), Filter.Gt(x => x.Remaining, 0)));
        if (categoryId is { } c)
        {
            filter = Filter.And(Filter.Eq(x => x.CategoryId, c), filter);
        }

        if (businessId is { } b)
        {
            filter = Filter.And(Filter.Eq(x => x.BusinessId, b), filter);
        }

        var find = Context.Session is { } session ? Collection.Find(session, Active(filter)) : Collection.Find(Active(filter));
        return await find.SortByDescending(x => x.CreatedAt).Limit(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}

public static class SocialCollectionNames
{
    public const string Offers = "offers";
    public const string Claims = "claims";
}

/// <summary>Sosial qonaqlıq sahəsinin DI qeydiyyatı.</summary>
public static class SocialRegistration
{
    public static IServiceCollection AddSocial(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IOfferRepository, OfferRepository>();
        services.AddScoped<OfferReferenceCheck>();
        return services;
    }
}
