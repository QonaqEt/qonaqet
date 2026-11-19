using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Common;
using Qonaqet.Application.Features.Categories.Common;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Social;
using Qonaqet.Domain.Verification;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Persistence.Mongo.Repositories;

public sealed class CategoryRepository(MongoContext context, IAggregateTracker tracker, IOutbox outbox, IClock clock)
    : SoftDeleteRepository<Category, CategoryId>(context, tracker, outbox, clock, CategoryCollectionNames.Categories), ICategoryRepository
{
    public async Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken)
    {
        var filter = Active(Filter.Eq(x => x.Code, code));
        var find = Context.Session is { } session ? Collection.Find(session, filter) : Collection.Find(filter);
        return await find.Limit(1).AnyAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Category>> ListActiveAsync(CancellationToken cancellationToken)
    {
        var items = await FindAsync(Filter.Eq(x => x.IsActive, true), cancellationToken).ConfigureAwait(false);
        return [.. items.OrderBy(c => c.SortOrder).ThenBy(c => c.Name, StringComparer.Ordinal)];
    }

    public async Task<PagedResult<Category>> ListForAdminAsync(bool includeDeleted, PageRequest page, CancellationToken cancellationToken)
    {
        var filter = includeDeleted ? Filter.Empty : Active();
        var find = Context.Session is { } session ? Collection.Find(session, filter) : Collection.Find(filter);
        var total = await find.CountDocumentsAsync(cancellationToken).ConfigureAwait(false);
        var items = await find.SortBy(x => x.SortOrder).ThenBy(x => x.Name).Skip(page.Skip).Limit(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PagedResult<Category>(items, total);
    }

    public async Task<Category?> GetDeletedAsync(CategoryId id, CancellationToken cancellationToken)
    {
        var found = await IncludeDeleted().FindAsync(Filter.And(ById(id), Filter.Eq(x => x.IsDeleted, true)), cancellationToken).ConfigureAwait(false);
        return found.Count == 0 ? null : found[0];
    }
}

public sealed class PoolRepository(MongoContext context, IAggregateTracker tracker, IOutbox outbox, IClock clock)
    : SoftDeleteRepository<Pool, PoolId>(context, tracker, outbox, clock, CategoryCollectionNames.Pools), IPoolRepository
{
    public async Task<Pool?> GetDefaultForCategoryAsync(CategoryId categoryId, CancellationToken cancellationToken)
    {
        var found = await FindAsync(Filter.And(Filter.Eq(x => x.CategoryId, categoryId), Filter.Eq(x => x.CampaignId, null)), cancellationToken)
            .ConfigureAwait(false);
        return found.Count == 0 ? null : found[0];
    }

    public Task<long> CascadeDeleteForCategoryAsync(CategoryId categoryId, Guid? userId, CancellationToken cancellationToken) =>
        CascadeSoftDeleteAsync(Filter.Eq(x => x.CategoryId, categoryId), categoryId.Value, userId, CategoryCascade.DeleteReason, cancellationToken);

    public Task<long> CascadeRestoreForCategoryAsync(CategoryId categoryId, Guid? userId, CancellationToken cancellationToken) =>
        CascadeRestoreAsync(Filter.Eq(x => x.CategoryId, categoryId), categoryId.Value, userId, CategoryCascade.DeleteReason, cancellationToken);
}

public sealed class VerificationRequestRepository(MongoContext context, IAggregateTracker tracker, IOutbox outbox, IClock clock)
    : SoftDeleteRepository<VerificationRequest, VerificationRequestId>(context, tracker, outbox, clock, CategoryCollectionNames.VerificationRequests),
        IVerificationRequestRepository
{
    public async Task<bool> HasPendingAsync(Guid userId, CategoryId categoryId, CancellationToken cancellationToken)
    {
        var filter = Active(Filter.And(
            Filter.Eq(x => x.UserId, userId), Filter.Eq(x => x.CategoryId, categoryId), Filter.Eq(x => x.Status, VerificationStatus.Pending)));
        var find = Context.Session is { } session ? Collection.Find(session, filter) : Collection.Find(filter);
        return await find.Limit(1).AnyAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<VerificationRequest>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        [.. (await FindAsync(Filter.Eq(x => x.UserId, userId), cancellationToken).ConfigureAwait(false)).OrderByDescending(r => r.CreatedAt)];
}

public sealed class IdentityHashRepository(MongoContext context, IAggregateTracker tracker, IOutbox outbox, IClock clock)
    : SoftDeleteRepository<IdentityHash, IdentityHashId>(context, tracker, outbox, clock, CategoryCollectionNames.IdentityHashes), IIdentityHashRepository
{
    public async Task<IdentityHash?> FindAsync(string finHash, CategoryId categoryId, CancellationToken cancellationToken)
    {
        var found = await FindAsync(Filter.And(Filter.Eq(x => x.FinHash, finHash), Filter.Eq(x => x.CategoryId, categoryId)), cancellationToken)
            .ConfigureAwait(false);
        return found.Count == 0 ? null : found[0];
    }
}

/// <summary>
/// Başqa kolleksiyaların kateqoriyaya istinadları. Sənədlər xam BSON kimi oxunur (təkliflər və kampaniyalar öz epic-lərində
/// gəlir); hər sorğu <c>isDeleted: false</c> daşıyır və partial indekslərə düşür.
/// </summary>
public sealed class MongoCategoryUsageReader(MongoContext context) : ICategoryUsageReader
{
    private static readonly FilterDefinitionBuilder<BsonDocument> F = Builders<BsonDocument>.Filter;

    public async Task<bool> IsInUseAsync(CategoryId categoryId, CancellationToken cancellationToken)
    {
        var id = new BsonBinaryData(categoryId.Value, GuidRepresentation.Standard);
        var byCategory = F.Eq("categoryId", id);
        return await AnyAsync(CategoryCollectionNames.VerificationRequests, byCategory, cancellationToken).ConfigureAwait(false)
            || await AnyAsync(CategoryCollectionNames.Users, F.Eq("verifiedCategories.categoryId", id), cancellationToken).ConfigureAwait(false)
            || await AnyAsync(CategoryCollectionNames.Offers, byCategory, cancellationToken).ConfigureAwait(false)
            || await AnyAsync(CategoryCollectionNames.Campaigns, byCategory, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CategoryDeleteBlockers> GetDeleteBlockersAsync(CategoryId categoryId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var id = new BsonBinaryData(categoryId.Value, GuidRepresentation.Standard);
        var byCategory = F.Eq("categoryId", id);
        var activeOffers = F.And(byCategory, F.Or(F.Eq("isActive", true), F.Gt("remaining", 0)));
        var activeCampaigns = F.And(byCategory, F.In("status", new BsonValue[] { "Draft", "Active" }));
        var poolBalance = F.And(byCategory, F.Or(F.Gt("balanceQepik", 0L), F.Gt("reservedQepik", 0L)));
        var validVerification = F.ElemMatch<BsonValue>(
            "verifiedCategories",
            new BsonDocument
            {
                { "categoryId", id },
                { "$or", new BsonArray { new BsonDocument("validUntil", BsonNull.Value), new BsonDocument("validUntil", new BsonDocument("$gt", new BsonDateTime(now.UtcDateTime))) } },
            });

        return new CategoryDeleteBlockers(
            await AnyAsync(CategoryCollectionNames.Offers, activeOffers, cancellationToken).ConfigureAwait(false),
            await AnyAsync(CategoryCollectionNames.Campaigns, activeCampaigns, cancellationToken).ConfigureAwait(false),
            await AnyAsync(CategoryCollectionNames.Pools, poolBalance, cancellationToken).ConfigureAwait(false),
            await AnyAsync(CategoryCollectionNames.Users, validVerification, cancellationToken).ConfigureAwait(false));
    }

    private async Task<bool> AnyAsync(string collectionName, FilterDefinition<BsonDocument> filter, CancellationToken cancellationToken)
    {
        var collection = context.Collection<BsonDocument>(collectionName);
        var active = F.And(F.Eq("isDeleted", false), filter);
        var find = context.Session is { } session ? collection.Find(session, active) : collection.Find(active);
        return await find.Limit(1).AnyAsync(cancellationToken).ConfigureAwait(false);
    }
}

public static class CategoryCollectionNames
{
    public const string Categories = "categories";
    public const string VerificationRequests = "verificationRequests";
    public const string Users = "users";
    public const string Offers = "offers";
    public const string Campaigns = "campaigns";
    public const string Pools = "pools";
    public const string IdentityHashes = "identityHashes";
}

/// <summary>Kateqoriya və təsdiq sahəsinin DI qeydiyyatı.</summary>
public static class CategoryRegistration
{
    public static IServiceCollection AddCategories(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<ICategoryUsageReader, MongoCategoryUsageReader>();
        services.AddScoped<IPoolRepository, PoolRepository>();
        services.AddScoped<IVerificationRequestRepository, VerificationRequestRepository>();
        services.AddScoped<IIdentityHashRepository, IdentityHashRepository>();
        services.AddScoped<CategoryIconCheck>();
        return services;
    }
}
