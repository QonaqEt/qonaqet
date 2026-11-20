using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Application.Abstractions.Persistence;

namespace Qonaqet.Infrastructure.Persistence.Mongo.Repositories;

/// <summary>Aqreqat sorğular (Mongo aggregation); yalnız saylar və cəmlər, heç bir sənəd/şəxs qaytarılmır.</summary>
public sealed class SocialStatsReader(IMongoDatabase database) : ISocialStatsReader
{
    private static readonly BsonDocument NotDeleted = new("isDeleted", false);

    public Task<long> CountRedeemedClaimsSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) =>
        Claims.CountDocumentsAsync(
            new BsonDocument { { "isDeleted", false }, { "status", "Redeemed" }, { "redeemedAt", new BsonDocument("$gte", since.UtcDateTime) } },
            cancellationToken: cancellationToken);

    public async Task<long> CountPeopleTreatedAsync(CancellationToken cancellationToken)
    {
        var result = await Claims.Aggregate()
            .Match(new BsonDocument { { "isDeleted", false }, { "status", "Redeemed" } })
            .Group(new BsonDocument("_id", "$recipientUserId"))
            .Count()
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return result?.Count ?? 0;
    }

    public Task<long> CountActiveBusinessesAsync(CancellationToken cancellationToken) =>
        database.GetCollection<BsonDocument>(BusinessCollectionNames.Businesses)
            .CountDocumentsAsync(new BsonDocument { { "isDeleted", false }, { "status", "Active" } }, cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<PoolSummaryRow>> PoolSummaryAsync(CancellationToken cancellationToken)
    {
        var pools = await database.GetCollection<BsonDocument>(CategoryCollectionNames.Pools).Find(NotDeleted).ToListAsync(cancellationToken).ConfigureAwait(false);
        var donated = await database.GetCollection<BsonDocument>(DonationCollectionNames.PoolDonations).Aggregate()
            .Match(new BsonDocument { { "isDeleted", false }, { "status", "Confirmed" } })
            .Group(new BsonDocument { { "_id", "$poolId" }, { "sum", new BsonDocument("$sum", "$amountQepik") } })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var treats = await Claims.Aggregate()
            .Match(new BsonDocument { { "isDeleted", false }, { "status", "Redeemed" }, { "source", "Pool" } })
            .Group(new BsonDocument { { "_id", "$poolId" }, { "count", new BsonDocument("$sum", 1) } })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var donatedByPool = donated.ToDictionary(d => d["_id"].AsGuid, d => d["sum"].ToInt64());
        var treatsByPool = treats.Where(t => !t["_id"].IsBsonNull).ToDictionary(t => t["_id"].AsGuid, t => t["count"].ToInt64());

        return [.. pools
            .GroupBy(p => p["categoryId"].AsGuid)
            .Select(g => new PoolSummaryRow(
                g.Key,
                g.Sum(p => p["balanceQepik"].ToInt64() - p["reservedQepik"].ToInt64()),
                g.Sum(p => donatedByPool.GetValueOrDefault(p["_id"].AsGuid)),
                g.Sum(p => treatsByPool.GetValueOrDefault(p["_id"].AsGuid))))];
    }

    private IMongoCollection<BsonDocument> Claims => database.GetCollection<BsonDocument>(SocialCollectionNames.Claims);
}
