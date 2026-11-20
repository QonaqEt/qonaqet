using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Gifts.Commands.CreateGift;
using Qonaqet.Application.Features.Gifts.Common;
using Qonaqet.Application.Features.Gifts.Events;
using Qonaqet.Domain.Gifts;
using Qonaqet.Infrastructure.Identity;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Persistence.Mongo.Repositories;

public sealed class GiftRepository(MongoContext context, IAggregateTracker tracker, IOutbox outbox, IClock clock)
    : SoftDeleteRepository<Gift, GiftId>(context, tracker, outbox, clock, GiftCollectionNames.Gifts), IGiftRepository
{
    public async Task<bool> TryReplaceAsync(Gift gift, long expectedVersion, CancellationToken cancellationToken)
    {
        try
        {
            return await TryReplaceAsync(gift, Filter.Eq(x => x.Version, expectedVersion), cancellationToken).ConfigureAwait(false);
        }
        catch (MongoException ex) when (Context.Session is not null && ex.HasErrorLabel("TransientTransactionError"))
        {
            // Paralel tranzaksiya eyni hədiyyəni elə indi dəyişir (WriteConflict): bu cəhd uduzdu.
            return false;
        }
    }

    public async Task<Gift?> FindByLinkHashAsync(string linkTokenHash, CancellationToken cancellationToken) =>
        (await FindAsync(Filter.Eq(x => x.LinkTokenHash, linkTokenHash), cancellationToken).ConfigureAwait(false)).FirstOrDefault();

    public async Task<Gift?> FindByShortCodeAsync(string shortCodeHash, CancellationToken cancellationToken) =>
        (await FindAsync(Filter.Eq(x => x.ShortCodeHash, shortCodeHash), cancellationToken).ConfigureAwait(false)).FirstOrDefault();

    public async Task<IReadOnlyList<Gift>> ListSentAsync(Guid senderUserId, int limit, CancellationToken cancellationToken) =>
        await Collection.Find(Active(Filter.Eq(x => x.SenderUserId, senderUserId))).SortByDescending(x => x.CreatedAt).Limit(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Gift>> ListDueForDeliveryAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken) =>
        await Collection.Find(Active(Filter.And(Filter.Eq(x => x.Status, GiftStatus.Scheduled), Filter.Lte(x => x.DeliverAt, now))))
            .SortBy(x => x.DeliverAt).Limit(limit).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Gift>> ListReceivedAsync(Guid recipientUserId, int limit, CancellationToken cancellationToken)
    {
        var delivered = new[]
        {
            GiftStatus.Delivered, GiftStatus.Opened, GiftStatus.PartiallyRedeemed, GiftStatus.Redeemed, GiftStatus.Expired, GiftStatus.Refunded,
        };
        var filter = Active(Filter.And(Filter.Eq(x => x.RecipientUserId, recipientUserId), Filter.In(x => x.Status, delivered)));
        return await Collection.Find(filter).SortByDescending(x => x.DeliverAt).Limit(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary><c>giftRedemptions</c>: yalnız əlavə və oxu (append-only).</summary>
public sealed class GiftRedemptionRepository(MongoContext context) : IGiftRedemptionRepository
{
    private readonly IMongoCollection<GiftRedemption> _collection = context.Collection<GiftRedemption>(GiftCollectionNames.GiftRedemptions);

    public Task AddAsync(GiftRedemption redemption, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(redemption);
        return context.Session is { } session
            ? _collection.InsertOneAsync(session, redemption, cancellationToken: cancellationToken)
            : _collection.InsertOneAsync(redemption, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<GiftRedemption>> ListForGiftAsync(GiftId giftId, CancellationToken cancellationToken) =>
        await _collection.Find(Builders<GiftRedemption>.Filter.And(
                Builders<GiftRedemption>.Filter.Eq(x => x.IsDeleted, false), Builders<GiftRedemption>.Filter.Eq(x => x.GiftId, giftId)))
            .SortBy(x => x.CreatedAt).ToListAsync(cancellationToken).ConfigureAwait(false);
}

public static class GiftCollectionNames
{
    public const string Gifts = "gifts";
    public const string GiftRedemptions = "giftRedemptions";
}

/// <summary>Hədiyyə sahəsinin DI qeydiyyatı.</summary>
public static class GiftRegistration
{
    public static IServiceCollection AddGifts(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<GiftOptions>().Bind(configuration.GetSection(GiftOptions.SectionName));
        services.AddScoped<IGiftRepository, GiftRepository>();
        services.AddScoped<IGiftRedemptionRepository, GiftRedemptionRepository>();
        services.AddSingleton<IGiftLinkTokens, GiftLinkTokens>();
        services.AddScoped<GiftPricing>();
        services.AddScoped<GiftExpiryPools>();
        services.AddScoped<GiftFulfillment>();
        services.AddScoped<GiftPresenter>();
        services.AddScoped<GiftNotifications>();
        services.AddScoped<GiftTransactions>();
        return services;
    }
}
