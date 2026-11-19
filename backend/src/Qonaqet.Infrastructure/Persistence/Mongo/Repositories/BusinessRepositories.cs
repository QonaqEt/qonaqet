using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Common;
using Qonaqet.Application.Features.Businesses.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Persistence.Mongo.Repositories;

public sealed class BusinessRepository(MongoContext context, IAggregateTracker tracker, IOutbox outbox, IClock clock)
    : SoftDeleteRepository<Business, BusinessId>(context, tracker, outbox, clock, BusinessCollectionNames.Businesses), IBusinessRepository
{
    public async Task<Business?> GetBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        var found = await FindAsync(Filter.Eq(x => x.Slug, slug), cancellationToken).ConfigureAwait(false);
        return found.Count == 0 ? null : found[0];
    }

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken) =>
        ExistsAsync(Filter.Eq(x => x.Slug, slug), cancellationToken);

    public Task<bool> VoenExistsAsync(string voen, CancellationToken cancellationToken) =>
        ExistsAsync(Filter.Eq(x => x.Voen, voen), cancellationToken);

    public Task<bool> HasPendingApplicationAsync(Guid applicantUserId, CancellationToken cancellationToken) =>
        ExistsAsync(Filter.And(Filter.Eq(x => x.OwnerUserId, applicantUserId), Filter.Eq(x => x.Status, BusinessStatus.Pending)), cancellationToken);

    public async Task<PagedResult<Business>> ListForAdminAsync(BusinessStatus? status, bool includeDeleted, PageRequest page, CancellationToken cancellationToken)
    {
        var filter = status is { } s ? Filter.Eq(x => x.Status, s) : Filter.Empty;
        if (!includeDeleted)
        {
            filter = Active(filter);
        }

        var find = Context.Session is { } session ? Collection.Find(session, filter) : Collection.Find(filter);
        var total = await find.CountDocumentsAsync(cancellationToken).ConfigureAwait(false);
        var items = await find.SortByDescending(x => x.CreatedAt).Skip(page.Skip).Limit(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PagedResult<Business>(items, total);
    }

    private async Task<bool> ExistsAsync(FilterDefinition<Business> filter, CancellationToken cancellationToken)
    {
        var find = Context.Session is { } session ? Collection.Find(session, Active(filter)) : Collection.Find(Active(filter));
        return await find.Limit(1).AnyAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class BusinessMembershipRepository(MongoContext context, IAggregateTracker tracker, IOutbox outbox, IClock clock)
    : SoftDeleteRepository<BusinessMembership, BusinessMembershipId>(context, tracker, outbox, clock, BusinessCollectionNames.Memberships),
        IBusinessMembershipRepository
{
    public async Task<BusinessMembership?> FindAsync(BusinessId businessId, Guid userId, CancellationToken cancellationToken)
    {
        var found = await FindAsync(Filter.And(Filter.Eq(x => x.BusinessId, businessId), Filter.Eq(x => x.UserId, userId)), cancellationToken).ConfigureAwait(false);
        return found.Count == 0 ? null : found[0];
    }

    public async Task<IReadOnlyList<BusinessMembership>> ListForBusinessAsync(BusinessId businessId, CancellationToken cancellationToken) =>
        [.. (await FindAsync(Filter.Eq(x => x.BusinessId, businessId), cancellationToken).ConfigureAwait(false)).OrderBy(m => m.CreatedAt)];

    public async Task<IReadOnlyList<BusinessMembership>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        [.. (await FindAsync(Filter.Eq(x => x.UserId, userId), cancellationToken).ConfigureAwait(false)).OrderBy(m => m.CreatedAt)];
}

public static class BusinessCollectionNames
{
    public const string Businesses = "businesses";
    public const string Memberships = "businessMemberships";
}

/// <summary>Biznes və kataloq sahəsinin DI qeydiyyatı.</summary>
public static class BusinessRegistration
{
    public static IServiceCollection AddBusinesses(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IBusinessRepository, BusinessRepository>();
        services.AddScoped<IBusinessMembershipRepository, BusinessMembershipRepository>();
        services.AddScoped<BusinessAccess>();
        services.AddScoped<BusinessImageCheck>();
        services.AddScoped<BusinessAccessSync>();
        return services;
    }
}
