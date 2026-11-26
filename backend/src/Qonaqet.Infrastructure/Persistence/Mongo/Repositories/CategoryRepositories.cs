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

    public async Task<Category?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
        (await FindAsync(Filter.Eq(x => x.Code, code), cancellationToken).ConfigureAwait(false)).FirstOrDefault();

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

    public async Task<bool> TryReserveAsync(PoolId poolId, long amountQepik, CancellationToken cancellationToken)
    {
        var available = new BsonDocumentFilterDefinition<Pool>(new BsonDocument("$expr", new BsonDocument("$gte", new BsonArray
        {
            new BsonDocument("$subtract", new BsonArray { "$balanceQepik", "$reservedQepik" }), amountQepik,
        })));
        var result = await Collection.UpdateOneAsync(
            Active(Filter.And(ById(poolId), available)),
            Builders<Pool>.Update.Inc(x => x.ReservedQepik, amountQepik).Set(x => x.UpdatedAt, Clock.UtcNow),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.ModifiedCount == 1;
    }

    public async Task<bool> ReleaseReservationAsync(PoolId poolId, long amountQepik, CancellationToken cancellationToken)
    {
        var result = await Collection.UpdateOneAsync(
            Filter.And(ById(poolId), Filter.Gte(x => x.ReservedQepik, amountQepik)),
            Builders<Pool>.Update.Inc(x => x.ReservedQepik, -amountQepik).Set(x => x.UpdatedAt, Clock.UtcNow),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.ModifiedCount == 1;
    }

    public async Task<bool> CreditAsync(PoolId poolId, long amountQepik, CancellationToken cancellationToken)
    {
        var update = Builders<Pool>.Update.Inc(x => x.BalanceQepik, Guard.Positive(amountQepik)).Set(x => x.UpdatedAt, Clock.UtcNow);
        var result = Context.Session is { } session
            ? await Collection.UpdateOneAsync(session, ById(poolId), update, cancellationToken: cancellationToken).ConfigureAwait(false)
            : await Collection.UpdateOneAsync(ById(poolId), update, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.ModifiedCount == 1;
    }

    public async Task<bool> ConsumeReservationAsync(PoolId poolId, long amountQepik, CancellationToken cancellationToken)
    {
        var filter = Filter.And(ById(poolId), Filter.Gte(x => x.ReservedQepik, amountQepik), Filter.Gte(x => x.BalanceQepik, amountQepik));
        var update = Builders<Pool>.Update.Inc(x => x.ReservedQepik, -amountQepik).Inc(x => x.BalanceQepik, -amountQepik).Set(x => x.UpdatedAt, Clock.UtcNow);
        var result = Context.Session is { } session
            ? await Collection.UpdateOneAsync(session, filter, update, cancellationToken: cancellationToken).ConfigureAwait(false)
            : await Collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.ModifiedCount == 1;
    }

    public async Task<bool> RestoreReservationAsync(PoolId poolId, long amountQepik, CancellationToken cancellationToken)
    {
        var update = Builders<Pool>.Update
            .Inc(x => x.ReservedQepik, Guard.Positive(amountQepik)).Inc(x => x.BalanceQepik, amountQepik).Set(x => x.UpdatedAt, Clock.UtcNow);
        var result = Context.Session is { } session
            ? await Collection.UpdateOneAsync(session, ById(poolId), update, cancellationToken: cancellationToken).ConfigureAwait(false)
            : await Collection.UpdateOneAsync(ById(poolId), update, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.ModifiedCount == 1;
    }

    public async Task<IReadOnlyList<Pool>> ListClaimablePoolsAsync(IReadOnlyCollection<CategoryId> categoryIds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (categoryIds.Count == 0)
        {
            return [];
        }

        var ids = categoryIds.Select(c => new BsonBinaryData(c.Value, GuidRepresentation.Standard)).ToArray();
        // Aktiv kampaniyalar (sənədin özü E08-T05-dədir): status Active, indi [startsAt, endsAt) arasında.
        var campaigns = await Context.Collection<BsonDocument>("campaigns").Find(new BsonDocument
            {
                { "isDeleted", false },
                { "status", "Active" },
                { "categoryId", new BsonDocument("$in", new BsonArray(ids)) },
                { "startsAt", new BsonDocument("$lte", now.UtcDateTime) },
                { "endsAt", new BsonDocument("$gt", now.UtcDateTime) },
            })
            .Sort(new BsonDocument("endsAt", 1))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var campaignOrder = campaigns.Where(c => c.Contains("poolId") && !c["poolId"].IsBsonNull).Select(c => c["poolId"].AsGuid).ToList();

        var pools = await FindAsync(Filter.In(x => x.CategoryId, categoryIds), cancellationToken).ConfigureAwait(false);
        var byId = pools.ToDictionary(p => p.Id.Value);
        var ordered = campaignOrder.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        ordered.AddRange(pools.Where(p => p.IsDefault));
        return ordered;
    }

    public async Task<long> RollOverAsync(PoolId fromPoolId, PoolId toPoolId, CampaignId campaignId, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await Context.InTransactionAsync(async ct =>
                {
                    var session = Context.Session!;
                    var from = await Collection.Find(session, ById(fromPoolId)).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                    var amount = from is null ? 0 : from.BalanceQepik - from.ReservedQepik;
                    if (amount <= 0)
                    {
                        return 0L;
                    }

                    var now = Clock.UtcNow;
                    var debit = await Collection.UpdateOneAsync(
                        session,
                        Filter.And(ById(fromPoolId), Filter.Eq(x => x.BalanceQepik, from!.BalanceQepik), Filter.Eq(x => x.ReservedQepik, from.ReservedQepik)),
                        Builders<Pool>.Update.Inc(x => x.BalanceQepik, -amount).Set(x => x.UpdatedAt, now),
                        cancellationToken: ct).ConfigureAwait(false);
                    if (debit.ModifiedCount != 1)
                    {
                        return 0L;
                    }

                    await Collection.UpdateOneAsync(
                        session, ById(toPoolId), Builders<Pool>.Update.Inc(x => x.BalanceQepik, amount).Set(x => x.UpdatedAt, now), cancellationToken: ct)
                        .ConfigureAwait(false);
                    await Context.Collection<BsonDocument>(DonationCollectionNames.Campaigns).UpdateOneAsync(
                        session,
                        new BsonDocument("_id", new BsonBinaryData(campaignId.Value, GuidRepresentation.Standard)),
                        new BsonDocument("$inc", new BsonDocument("rolledOverQepik", amount)),
                        cancellationToken: ct).ConfigureAwait(false);
                    await Outbox.AddAsync([new CampaignRolledOver(campaignId, fromPoolId, toPoolId, amount, now)], ct).ConfigureAwait(false);
                    return amount;
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (MongoException ex) when (ex.HasErrorLabel("TransientTransactionError") && attempt < 8)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task<IReadOnlyList<PoolBalance>> ListBalancesAsync(CancellationToken cancellationToken)
    {
        var pools = await FindAsync(Filter.Empty, cancellationToken).ConfigureAwait(false);
        return [.. pools.Select(p => new PoolBalance(p.Id, p.BalanceQepik, p.ReservedQepik))];
    }
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

    public async Task<PagedResult<VerificationRequest>> ListForAdminAsync(
        VerificationStatus? status, CategoryId? categoryId, PageRequest page, CancellationToken cancellationToken)
    {
        var filter = Filter.Empty;
        if (status is { } st)
        {
            filter = Filter.And(filter, Filter.Eq(x => x.Status, st));
        }

        if (categoryId is { } category)
        {
            filter = Filter.And(filter, Filter.Eq(x => x.CategoryId, category));
        }

        var active = Active(filter);
        var find = Context.Session is { } session ? Collection.Find(session, active) : Collection.Find(active);
        var total = await find.CountDocumentsAsync(cancellationToken).ConfigureAwait(false);
        var items = await find.SortBy(x => x.CreatedAt).Skip(page.Skip).Limit(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PagedResult<VerificationRequest>(items, total);
    }
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
/// Xatırlatmalar <c>users</c> kolleksiyasında birbaşa (sənədi əvəz etmədən) işləyir: <c>$elemMatch</c> ilə tapılır, mövqe operatoru
/// (<c>verifiedCategories.$.reminderSentFor</c>) ilə atomik qeyd olunur ki, paralel profil dəyişiklikləri itməsin.
/// </summary>
public sealed class MongoVerificationReminderStore(MongoContext context) : IVerificationReminderStore
{
    private static readonly FilterDefinitionBuilder<BsonDocument> F = Builders<BsonDocument>.Filter;

    private IMongoCollection<BsonDocument> Users => context.Collection<BsonDocument>(CategoryCollectionNames.Users);

    public async Task<IReadOnlyList<DueVerificationReminder>> FindDueAsync(DateTimeOffset now, DateTimeOffset until, int limit, CancellationToken cancellationToken)
    {
        var window = new BsonDocument { { "$gt", new BsonDateTime(now.UtcDateTime) }, { "$lte", new BsonDateTime(until.UtcDateTime) } };
        var filter = F.And(
            F.Eq("isDeleted", false),
            F.ElemMatch<BsonValue>("verifiedCategories", new BsonDocument("validUntil", window)));
        var users = await Users.Find(filter).Limit(limit).ToListAsync(cancellationToken).ConfigureAwait(false);

        var due = new List<DueVerificationReminder>();
        foreach (var user in users)
        {
            foreach (var entry in user.GetValue("verifiedCategories", new BsonArray()).AsBsonArray.Select(v => v.AsBsonDocument))
            {
                if (entry.GetValue("validUntil", BsonNull.Value) is not BsonDateTime validUntil)
                {
                    continue;
                }

                var at = new DateTimeOffset(validUntil.ToUniversalTime(), TimeSpan.Zero);
                var sentFor = entry.GetValue("reminderSentFor", BsonNull.Value);
                if (at > now && at <= until && !(sentFor is BsonDateTime sent && sent.ToUniversalTime() == validUntil.ToUniversalTime()))
                {
                    due.Add(new DueVerificationReminder(user["_id"].AsGuid, entry["categoryId"].AsGuid, at));
                }
            }
        }

        return due;
    }

    public async Task<bool> TryMarkSentAsync(DueVerificationReminder reminder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reminder);
        var validUntil = new BsonDateTime(reminder.ValidUntil.UtcDateTime);
        var filter = F.And(
            F.Eq("_id", new BsonBinaryData(reminder.UserId, GuidRepresentation.Standard)),
            F.Eq("isDeleted", false),
            F.ElemMatch<BsonValue>("verifiedCategories", new BsonDocument
            {
                { "categoryId", new BsonBinaryData(reminder.CategoryId, GuidRepresentation.Standard) },
                { "validUntil", validUntil },
                { "reminderSentFor", new BsonDocument("$ne", validUntil) },
            }));
        var update = Builders<BsonDocument>.Update.Set("verifiedCategories.$.reminderSentFor", validUntil);
        var result = await Users.UpdateOneAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.ModifiedCount == 1;
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
        // Yalnız aktiv kampaniya mane olur: qaralamanın (seed şablonları daxil) hovuzu və pulu yoxdur, kateqoriyası dəyişdirilə bilər.
        var activeCampaigns = F.And(byCategory, F.Eq("status", "Active"));
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
        services.AddScoped<Application.Features.Admin.Commands.ReviewVerification.VerificationReview>();
        services.AddScoped<IVerificationReminderStore, MongoVerificationReminderStore>();
        services.AddScoped<CategoryIconCheck>();
        return services;
    }
}
