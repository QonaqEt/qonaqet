using MongoDB.Driver;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Domain.Social;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Persistence.Mongo.Repositories;

public sealed class PoolDonationRepository(MongoContext context, IAggregateTracker tracker, IOutbox outbox, IClock clock)
    : SoftDeleteRepository<PoolDonation, PoolDonationId>(context, tracker, outbox, clock, DonationCollectionNames.PoolDonations), IPoolDonationRepository
{
    private const int MaxTransactionAttempts = 8;

    public async Task<PoolDonation?> ConfirmAsync(PoolDonationId id, DateTimeOffset at, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await Context.InTransactionAsync(ct => ConfirmInTransactionAsync(id, at, ct), cancellationToken).ConfigureAwait(false);
            }
            catch (MongoException ex) when (ex.HasErrorLabel("TransientTransactionError") && attempt < MaxTransactionAttempts)
            {
                // Eyni hovuzu eyni anda başqa təsdiq yeniləyir (WriteConflict): kiçik gözləmə ilə təkrar.
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(5, 25) * attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task<bool> TryCancelPendingAsync(PoolDonationId id, CancellationToken cancellationToken)
    {
        var result = await Collection.UpdateOneAsync(
            Active(Filter.And(ById(id), Filter.Eq(x => x.Status, PoolDonationStatus.Pending))),
            Builders<PoolDonation>.Update.Set(x => x.Status, PoolDonationStatus.Cancelled).Set(x => x.UpdatedAt, Clock.UtcNow),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.ModifiedCount == 1;
    }

    public async Task<PoolDonation?> FindOldestUnusedAsync(PoolId poolId, CancellationToken cancellationToken)
    {
        var find = Context.Session is { } session ? Collection.Find(session, Unused(poolId)) : Collection.Find(Unused(poolId));
        return await find.SortBy(x => x.ConfirmedAt).ThenBy(x => x.Id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ConsumeAsync(PoolId poolId, long amountQepik, CancellationToken cancellationToken)
    {
        var left = amountQepik;
        while (left > 0)
        {
            var oldest = await FindOldestUnusedAsync(poolId, cancellationToken).ConfigureAwait(false);
            if (oldest is null)
            {
                return;
            }

            var take = Math.Min(left, oldest.RemainingQepik);
            var filter = Filter.And(ById(oldest.Id), Filter.Eq(x => x.RemainingQepik, oldest.RemainingQepik));
            var update = Builders<PoolDonation>.Update.Inc(x => x.RemainingQepik, -take).Set(x => x.UpdatedAt, Clock.UtcNow);
            var result = Context.Session is { } session
                ? await Collection.UpdateOneAsync(session, filter, update, cancellationToken: cancellationToken).ConfigureAwait(false)
                : await Collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result.ModifiedCount == 1)
            {
                left -= take;
            }
        }
    }

    private static FilterDefinition<PoolDonation> Unused(PoolId poolId) => Active(Filter.And(
        Filter.Eq(x => x.PoolId, poolId), Filter.Eq(x => x.Status, PoolDonationStatus.Confirmed), Filter.Gt(x => x.RemainingQepik, 0)));

    public async Task<IReadOnlyList<PoolDonation>> ListForDonorAsync(Guid donorUserId, int limit, CancellationToken cancellationToken) =>
        await Collection.Find(Active(Filter.Eq(x => x.DonorUserId, donorUserId)))
            .SortByDescending(x => x.CreatedAt).Limit(limit).ToListAsync(cancellationToken).ConfigureAwait(false);

    private async Task<PoolDonation?> ConfirmInTransactionAsync(PoolDonationId id, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var session = Context.Session!;
        var donation = await Collection.Find(session, Active(ById(id))).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (donation is null || !donation.Confirm(at))
        {
            return donation;
        }

        await Collection.ReplaceOneAsync(session, Active(Filter.And(ById(id), Filter.Eq(x => x.Status, PoolDonationStatus.Pending))), donation,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var pools = Context.Collection<Pool>(CategoryCollectionNames.Pools);
        var credited = await pools.UpdateOneAsync(
            session,
            Builders<Pool>.Filter.Eq(p => p.Id, donation.PoolId),
            Builders<Pool>.Update.Inc(p => p.BalanceQepik, donation.AmountQepik).Set(p => p.UpdatedAt, Clock.UtcNow),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (credited.MatchedCount != 1)
        {
            throw new InvalidOperationException($"Hovuz {donation.PoolId.Value} tapılmadı.");
        }

        if (donation.CampaignId is { } campaignId)
        {
            await Context.Collection<MongoDB.Bson.BsonDocument>(DonationCollectionNames.Campaigns).UpdateOneAsync(
                session,
                new MongoDB.Bson.BsonDocument("_id", new MongoDB.Bson.BsonBinaryData(campaignId, MongoDB.Bson.GuidRepresentation.Standard)),
                new MongoDB.Bson.BsonDocument("$inc", new MongoDB.Bson.BsonDocument("raisedQepik", donation.AmountQepik)),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return donation;
    }
}

public sealed class CampaignRepository(MongoContext context, IAggregateTracker tracker, IOutbox outbox, IClock clock)
    : SoftDeleteRepository<Campaign, CampaignId>(context, tracker, outbox, clock, DonationCollectionNames.Campaigns), ICampaignRepository
{
    public async Task<bool> SlugExistsAsync(string slug, CampaignId? except, CancellationToken cancellationToken)
    {
        var filter = Filter.Eq(x => x.Slug, slug);
        if (except is { } id)
        {
            filter = Filter.And(filter, Filter.Ne(x => x.Id, id));
        }

        return (await FindAsync(filter, cancellationToken).ConfigureAwait(false)).Count > 0;
    }

    public async Task<Campaign?> GetBySlugAsync(string slug, CancellationToken cancellationToken) =>
        (await FindAsync(Filter.Eq(x => x.Slug, slug), cancellationToken).ConfigureAwait(false)).FirstOrDefault();

    public async Task<IReadOnlyList<Campaign>> ListAsync(CampaignStatus? status, int limit, CancellationToken cancellationToken)
    {
        var filter = status is { } s ? Active(Filter.Eq(x => x.Status, s)) : Active();
        return await Collection.Find(filter).SortByDescending(x => x.CreatedAt).Limit(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Campaign>> ListOpenAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken) =>
        await Collection.Find(Active(Filter.And(Filter.Eq(x => x.Status, CampaignStatus.Active), Filter.Lte(x => x.StartsAt, now), Filter.Gt(x => x.EndsAt, now))))
            .SortBy(x => x.EndsAt).Limit(limit).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Campaign>> ListDueToEndAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken) =>
        await Collection.Find(Active(Filter.And(Filter.Eq(x => x.Status, CampaignStatus.Active), Filter.Lte(x => x.EndsAt, now))))
            .SortBy(x => x.EndsAt).Limit(limit).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<bool> TryEndAsync(CampaignId id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var result = await Collection.UpdateOneAsync(
            Active(Filter.And(ById(id), Filter.Eq(x => x.Status, CampaignStatus.Active))),
            Builders<Campaign>.Update.Set(x => x.Status, CampaignStatus.Ended).Set(x => x.EndedAt, now).Set(x => x.UpdatedAt, now),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.ModifiedCount == 1;
    }

    public async Task<IReadOnlyList<Campaign>> ListEndedAsync(int limit, CancellationToken cancellationToken) =>
        await Collection.Find(Active(Filter.Eq(x => x.Status, CampaignStatus.Ended))).SortByDescending(x => x.EndedAt).Limit(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
}

public static class DonationCollectionNames
{
    public const string PoolDonations = "poolDonations";
    public const string Campaigns = "campaigns";
}
