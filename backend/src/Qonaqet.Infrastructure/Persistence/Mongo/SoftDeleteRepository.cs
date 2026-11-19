using MongoDB.Driver;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Persistence.Mongo;

/// <summary>
/// Mongo repository-lərinin bazası. <b>Hər</b> sorğu <see cref="Active"/> vasitəsilə <c>isDeleted: false</c>
/// şərtini literal olaraq daşıyır (partial indekslər yalnız belə sorğularda işləyir). Silinmiş qeydlər
/// yalnız <see cref="IncludeDeleted"/> ilə — admin endpoint-ləri üçün — görünür. Fiziki silmə yoxdur.
/// </summary>
public abstract class SoftDeleteRepository<TAggregate, TId> : IRepository<TAggregate, TId>
    where TAggregate : AggregateRoot<TId>
    where TId : struct, IStronglyTypedId<TId>
{
    protected SoftDeleteRepository(MongoContext context, IAggregateTracker tracker, IOutbox outbox, IClock clock, string collectionName)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
        Tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        Outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        CollectionName = Guard.NotNullOrWhiteSpace(collectionName);
        Collection = context.Collection<TAggregate>(collectionName);
    }

    protected MongoContext Context { get; }

    protected IAggregateTracker Tracker { get; }

    protected IOutbox Outbox { get; }

    protected IClock Clock { get; }

    protected string CollectionName { get; }

    /// <summary>Xam kolleksiya. Oxu üçün birbaşa işlətməyin — <see cref="FindAsync"/> və ya <see cref="Active"/> istifadə edin.</summary>
    protected IMongoCollection<TAggregate> Collection { get; }

    protected static FilterDefinitionBuilder<TAggregate> Filter => Builders<TAggregate>.Filter;

    /// <summary>Filtrə <c>isDeleted: false</c> əlavə edir. Bütün oxu və yazılar bundan keçir.</summary>
    public static FilterDefinition<TAggregate> Active(FilterDefinition<TAggregate>? filter = null)
    {
        var notDeleted = Filter.Eq(x => x.IsDeleted, false);
        return filter is null ? notDeleted : Filter.And(notDeleted, filter);
    }

    protected static FilterDefinition<TAggregate> ById(TId id) => Filter.Eq(x => x.Id, id);

    public async Task<TAggregate?> GetAsync(TId id, CancellationToken cancellationToken)
    {
        var aggregate = await FirstOrDefaultAsync(Active(ById(id)), cancellationToken).ConfigureAwait(false);
        if (aggregate is not null)
        {
            Tracker.Track(aggregate);
        }

        return aggregate;
    }

    /// <summary>Silinməmiş aqreqatlar; yüklənənlər hadisələri üçün izlənir.</summary>
    protected async Task<IReadOnlyList<TAggregate>> FindAsync(FilterDefinition<TAggregate> filter, CancellationToken cancellationToken)
    {
        var cursor = Context.Session is { } session
            ? Collection.Find(session, Active(filter))
            : Collection.Find(Active(filter));
        var items = await cursor.ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var item in items)
        {
            Tracker.Track(item);
        }

        return items;
    }

    public async Task AddAsync(TAggregate aggregate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        if (Context.Session is { } session)
        {
            await Collection.InsertOneAsync(session, aggregate, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await Collection.InsertOneAsync(aggregate, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        Tracker.Track(aggregate);
    }

    public async Task UpdateAsync(TAggregate aggregate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        if (!await ReplaceAsync(Active(ById(aggregate.Id)), aggregate, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"{typeof(TAggregate).Name} {aggregate.Id.Value} tapılmadı və ya silinib.");
        }

        Tracker.Track(aggregate);
    }

    public Task<bool> SoftDeleteAsync(TId id, Guid? userId, string? reason, CancellationToken cancellationToken) =>
        Context.InTransactionAsync(
            async ct =>
            {
                var aggregate = await FirstOrDefaultAsync(Active(ById(id)), ct).ConfigureAwait(false);
                if (aggregate is null)
                {
                    return false;
                }

                var now = Clock.UtcNow;
                aggregate.SoftDelete(now, userId, reason);
                await ReplaceAsync(Active(ById(id)), aggregate, ct).ConfigureAwait(false);
                await AuditAsync(AuditActions.SoftDelete, id, userId, now, reason, ct).ConfigureAwait(false);
                return true;
            },
            cancellationToken);

    public Task<bool> RestoreAsync(TId id, Guid? userId, CancellationToken cancellationToken) =>
        Context.InTransactionAsync(
            async ct =>
            {
                var deleted = Filter.And(ById(id), Filter.Eq(x => x.IsDeleted, true));
                var aggregate = await FirstOrDefaultAsync(deleted, ct).ConfigureAwait(false);
                if (aggregate is null)
                {
                    return false;
                }

                var now = Clock.UtcNow;
                aggregate.Restore(now, userId);
                await ReplaceAsync(deleted, aggregate, ct).ConfigureAwait(false);
                await AuditAsync(AuditActions.Restore, id, userId, now, null, ct).ConfigureAwait(false);
                return true;
            },
            cancellationToken);

    /// <summary>
    /// Valideyn silinəndə uşaq qeydlərin kaskad soft delete-i (<paramref name="reason"/> sabitdir, bərpa onu axtarır).
    /// Bir audit qeydi yazılır (valideynin ID-si ilə). Silinən qeydlərin sayı.
    /// </summary>
    protected async Task<long> CascadeSoftDeleteAsync(
        FilterDefinition<TAggregate> filter, Guid parentId, Guid? userId, string reason, CancellationToken cancellationToken)
    {
        var now = Clock.UtcNow;
        var update = Builders<TAggregate>.Update
            .Set(x => x.IsDeleted, true).Set(x => x.DeletedAt, now).Set(x => x.DeletedBy, userId).Set(x => x.DeleteReason, reason)
            .Set(x => x.UpdatedAt, now).Set(x => x.UpdatedBy, userId);
        var count = await UpdateManyAsync(Active(filter), update, cancellationToken).ConfigureAwait(false);
        await CascadeAuditAsync(AuditActions.SoftDelete, parentId, userId, now, reason, count, cancellationToken).ConfigureAwait(false);
        return count;
    }

    /// <summary><see cref="CascadeSoftDeleteAsync"/> ilə (eyni səbəblə) silinmiş qeydləri bərpa edir; ayrıca silinmişlərə toxunmur.</summary>
    protected async Task<long> CascadeRestoreAsync(
        FilterDefinition<TAggregate> filter, Guid parentId, Guid? userId, string reason, CancellationToken cancellationToken)
    {
        var now = Clock.UtcNow;
        var deleted = Filter.And(filter, Filter.Eq(x => x.IsDeleted, true), Filter.Eq(x => x.DeleteReason, reason));
        var update = Builders<TAggregate>.Update
            .Set(x => x.IsDeleted, false).Set(x => x.DeletedAt, null).Set(x => x.DeletedBy, null).Set(x => x.DeleteReason, null)
            .Set(x => x.UpdatedAt, now).Set(x => x.UpdatedBy, userId);
        var count = await UpdateManyAsync(deleted, update, cancellationToken).ConfigureAwait(false);
        await CascadeAuditAsync(AuditActions.Restore, parentId, userId, now, reason, count, cancellationToken).ConfigureAwait(false);
        return count;
    }

    private async Task<long> UpdateManyAsync(FilterDefinition<TAggregate> filter, UpdateDefinition<TAggregate> update, CancellationToken cancellationToken)
    {
        var result = Context.Session is { } session
            ? await Collection.UpdateManyAsync(session, filter, update, cancellationToken: cancellationToken).ConfigureAwait(false)
            : await Collection.UpdateManyAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.ModifiedCount;
    }

    private Task CascadeAuditAsync(string action, Guid parentId, Guid? userId, DateTimeOffset at, string reason, long count, CancellationToken cancellationToken)
    {
        var data = new Dictionary<string, string?>
        {
            ["collection"] = CollectionName,
            ["reason"] = reason,
            ["count"] = count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["cascade"] = "true",
        };
        var entry = new AuditRecorded(at, userId, userId is null ? "System" : "User", action, typeof(TAggregate).Name, parentId, data);
        return Outbox.AddAsync([entry], cancellationToken);
    }

    /// <summary>
    /// Silinmiş qeydlər daxil olmaqla oxu — <b>yalnız admin</b> sorğuları üçün (arxitektura testi
    /// istifadəni <c>Features/Admin</c> və persistence qatı ilə məhdudlaşdırır). Yalnız oxuyur.
    /// </summary>
    public DeletedInclusiveView<TAggregate> IncludeDeleted() => new(Collection, Context);

    private async Task<TAggregate?> FirstOrDefaultAsync(FilterDefinition<TAggregate> filter, CancellationToken cancellationToken)
    {
        var find = Context.Session is { } session ? Collection.Find(session, filter) : Collection.Find(filter);
        return await find.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> ReplaceAsync(FilterDefinition<TAggregate> filter, TAggregate aggregate, CancellationToken cancellationToken)
    {
        var result = Context.Session is { } session
            ? await Collection.ReplaceOneAsync(session, filter, aggregate, cancellationToken: cancellationToken).ConfigureAwait(false)
            : await Collection.ReplaceOneAsync(filter, aggregate, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.MatchedCount == 1;
    }

    private Task AuditAsync(string action, TId id, Guid? userId, DateTimeOffset at, string? reason, CancellationToken cancellationToken)
    {
        var data = new Dictionary<string, string?> { ["collection"] = CollectionName };
        if (reason is not null)
        {
            data["reason"] = reason;
        }

        var entry = new AuditRecorded(at, userId, userId is null ? "System" : "User", action, typeof(TAggregate).Name, id.Value, data);
        return Outbox.AddAsync([entry], cancellationToken);
    }
}

/// <summary>Silinmiş qeydləri də görən, yalnız oxuyan görünüş (admin "include deleted" siyahıları).</summary>
public sealed class DeletedInclusiveView<T>
    where T : ISoftDeletable
{
    private readonly IMongoCollection<T> _collection;
    private readonly MongoContext _context;

    internal DeletedInclusiveView(IMongoCollection<T> collection, MongoContext context)
    {
        _collection = collection;
        _context = context;
    }

    public async Task<IReadOnlyList<T>> FindAsync(FilterDefinition<T> filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var find = _context.Session is { } session ? _collection.Find(session, filter) : _collection.Find(filter);
        return await find.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<long> CountAsync(FilterDefinition<T> filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return _context.Session is { } session
            ? _collection.CountDocumentsAsync(session, filter, cancellationToken: cancellationToken)
            : _collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
    }
}
