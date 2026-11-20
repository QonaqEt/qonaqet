using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Corporate.Common;
using Qonaqet.Domain.Corporate;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Persistence.Mongo.Repositories;

public sealed class CorporateRepository(MongoContext context, IAggregateTracker tracker, IOutbox outbox, IClock clock)
    : SoftDeleteRepository<Corporate, CorporateId>(context, tracker, outbox, clock, CorporateCollectionNames.Corporates), ICorporateRepository
{
    public async Task<bool> VoenExistsAsync(string voen, CorporateId? except, CancellationToken cancellationToken)
    {
        var filter = Filter.Eq(x => x.Voen, voen);
        if (except is { } id)
        {
            filter = Filter.And(filter, Filter.Ne(x => x.Id, id));
        }

        return await Collection.Find(Active(filter)).Limit(1).AnyAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Corporate>> ListAsync(int limit, CancellationToken cancellationToken) =>
        await Collection.Find(Active()).SortBy(x => x.Name).Limit(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
}

public sealed class CorporateGiftOrderRepository(MongoContext context, IAggregateTracker tracker, IOutbox outbox, IClock clock)
    : SoftDeleteRepository<CorporateGiftOrder, CorporateGiftOrderId>(context, tracker, outbox, clock, CorporateCollectionNames.Orders), ICorporateGiftOrderRepository
{
    public async Task<bool> TryReplaceAsync(CorporateGiftOrder order, CorporateOrderStatus expected, int expectedSent, CancellationToken cancellationToken)
    {
        try
        {
            return await TryReplaceAsync(order, Filter.And(Filter.Eq(x => x.Status, expected), Filter.Eq(x => x.SentCount, expectedSent)), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (MongoException ex) when (Context.Session is not null && ex.HasErrorLabel("TransientTransactionError"))
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<CorporateGiftOrder>> ListForCorporateAsync(CorporateId corporateId, int limit, CancellationToken cancellationToken) =>
        await Collection.Find(Active(Filter.Eq(x => x.CorporateId, corporateId))).SortByDescending(x => x.CreatedAt).Limit(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<CorporateGiftOrder>> ListToFanOutAsync(int limit, CancellationToken cancellationToken) =>
        await Collection.Find(Active(Filter.In(x => x.Status, [CorporateOrderStatus.Paid, CorporateOrderStatus.Sending])))
            .SortBy(x => x.PaidAt).Limit(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
}

public sealed class CorporateReportRepository(MongoContext context, IAggregateTracker tracker, IOutbox outbox, IClock clock)
    : SoftDeleteRepository<CorporateReport, CorporateReportId>(context, tracker, outbox, clock, CorporateCollectionNames.Reports), ICorporateReportRepository;

/// <summary>Kampaniyanın CSR aqreqatları: təsdiqlənmiş ianələr və kampaniya hovuzundan maliyyələşən təhvil verilmiş claim-lər (yalnız saylar/məbləğlər).</summary>
public sealed class CsrStatsReader(MongoContext context) : ICsrStatsReader
{
    public async Task<CampaignCsrStats> CampaignStatsAsync(Guid campaignId, CancellationToken cancellationToken)
    {
        var id = new BsonBinaryData(campaignId, GuidRepresentation.Standard);
        var donations = await context.Collection<BsonDocument>(DonationCollectionNames.PoolDonations).CountDocumentsAsync(
            new BsonDocument { { "isDeleted", false }, { "campaignId", id }, { "status", "Confirmed" } }, cancellationToken: cancellationToken).ConfigureAwait(false);

        var poolIds = await context.Collection<BsonDocument>(CategoryCollectionNames.Pools)
            .Find(new BsonDocument { { "isDeleted", false }, { "campaignId", id } }).Project(new BsonDocument("_id", 1)).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (poolIds.Count == 0)
        {
            return new CampaignCsrStats((int)donations, 0, 0);
        }

        var claims = await context.Collection<BsonDocument>(SocialCollectionNames.Claims)
            .Find(new BsonDocument
            {
                { "isDeleted", false },
                { "poolId", new BsonDocument("$in", new BsonArray(poolIds.Select(p => p["_id"]))) },
                { "status", "Redeemed" },
            })
            .Project(new BsonDocument("amountQepik", 1)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new CampaignCsrStats(
            (int)donations, claims.Count, claims.Sum(c => c.TryGetValue("amountQepik", out var v) && v.IsNumeric ? v.ToInt64() : 0));
    }
}

public static class CorporateCollectionNames
{
    public const string Corporates = "corporates";
    public const string Orders = "corporateGiftOrders";
    public const string Reports = "corporateReports";
}

/// <summary>Korporativ sahənin DI qeydiyyatı.</summary>
public static class CorporateRegistration
{
    public static IServiceCollection AddCorporate(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<ICorporateRepository, CorporateRepository>();
        services.AddScoped<ICorporateGiftOrderRepository, CorporateGiftOrderRepository>();
        services.AddScoped<ICorporateReportRepository, CorporateReportRepository>();
        services.AddScoped<ICsrStatsReader, CsrStatsReader>();
        services.AddScoped<CorporateAccess>();
        services.AddScoped<Application.Features.Corporate.Queries.CorporateReports.CorporateCampaigns>();
        services.AddScoped<CorporateReportBuilder>();
        services.AddScoped<Application.Features.Corporate.Events.CorporateOrderPayments>();
        return services;
    }
}
