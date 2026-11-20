using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Social.Common;
using Qonaqet.Application.Features.Social.Events;
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

    // Vahid götürmə/qaytarma qəsdən sessiyasızdır (cari tranzaksiyadan kənarda): tək sənədli şərtli yeniləmə Mongo-da atomikdir,
    // paralel yazılar bir-birini gözləyir. Tranzaksiya daxilində isti sənəd hər paralel claim-də WriteConflict verərdi.
    public async Task<bool> TryTakeUnitAsync(Offer offer, string bakuDateKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offer);
        var open = Active(Filter.And(ById(offer.Id), Filter.Eq(x => x.IsActive, true), Filter.Eq(x => x.Type, offer.Type)));
        if (offer.Type != OfferType.BusinessDaily)
        {
            return await TakeAsync(
                Filter.And(open, Filter.Gt(x => x.Remaining, 0)), Builders<Offer>.Update.Inc(x => x.Remaining, -1), cancellationToken).ConfigureAwait(false);
        }

        // Bu gün üçün sayğac varsa: usedToday < dailyQuota → +1.
        var sameDay = Filter.And(
            open, Filter.Eq(x => x.QuotaDate, bakuDateKey),
            new BsonDocumentFilterDefinition<Offer>(new BsonDocument("$expr", new BsonDocument("$lt", new BsonArray { "$usedToday", "$dailyQuota" }))));
        var increment = Builders<Offer>.Update.Inc(x => x.UsedToday, 1);
        if (await TakeAsync(sameDay, increment, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        // Lazy sıfırlama: sayğac köhnə gündədir → bu günə keçir və 1 olur. Paralel sıfırlamalardan yalnız biri uyğun gəlir,
        // qalanları yenidən bugünkü sayğacı yoxlayır.
        var newDay = Filter.And(open, Filter.Ne(x => x.QuotaDate, bakuDateKey), Filter.Gte(x => x.DailyQuota, 1));
        var reset = Builders<Offer>.Update.Set(x => x.QuotaDate, bakuDateKey).Set(x => x.UsedToday, 1);
        return await TakeAsync(newDay, reset, cancellationToken).ConfigureAwait(false)
            || await TakeAsync(sameDay, increment, cancellationToken).ConfigureAwait(false);
    }

    public async Task ReleaseUnitAsync(OfferId offerId, OfferType type, string? quotaDate, CancellationToken cancellationToken)
    {
        if (type == OfferType.BusinessDaily)
        {
            if (quotaDate is null)
            {
                return;
            }

            await TakeAsync(
                Active(Filter.And(ById(offerId), Filter.Eq(x => x.QuotaDate, quotaDate), Filter.Gt(x => x.UsedToday, 0))),
                Builders<Offer>.Update.Inc(x => x.UsedToday, -1), cancellationToken).ConfigureAwait(false);
            return;
        }

        await TakeAsync(
            Active(Filter.And(
                ById(offerId),
                new BsonDocumentFilterDefinition<Offer>(new BsonDocument("$expr", new BsonDocument("$lt", new BsonArray { "$remaining", "$totalQuantity" }))))),
            Builders<Offer>.Update.Inc(x => x.Remaining, 1), cancellationToken).ConfigureAwait(false);
    }

    public Task<long> CascadeDeleteForBusinessAsync(BusinessId businessId, Guid? userId, CancellationToken cancellationToken) =>
        CascadeSoftDeleteAsync(Filter.Eq(x => x.BusinessId, businessId), businessId.Value, userId, BusinessCascade.DeleteReason, cancellationToken);

    public Task<long> CascadeRestoreForBusinessAsync(BusinessId businessId, Guid? userId, CancellationToken cancellationToken) =>
        CascadeRestoreAsync(Filter.Eq(x => x.BusinessId, businessId), businessId.Value, userId, BusinessCascade.DeleteReason, cancellationToken);

    public const string UnpaidReason = "payment_not_completed";

    public async Task<bool> TryActivateRemoteAsync(OfferId offerId, CancellationToken cancellationToken) =>
        await TakeAsync(
            Active(Filter.And(ById(offerId), Filter.Eq(x => x.Type, OfferType.Remote), Filter.Eq(x => x.AwaitingPayment, true))),
            Builders<Offer>.Update.Set(x => x.AwaitingPayment, false).Set(x => x.IsActive, true),
            cancellationToken).ConfigureAwait(false);

    public async Task<bool> TryCloseUnpaidRemoteAsync(OfferId offerId, CancellationToken cancellationToken)
    {
        var now = Clock.UtcNow;
        return await TakeAsync(
            Active(Filter.And(ById(offerId), Filter.Eq(x => x.Type, OfferType.Remote), Filter.Eq(x => x.AwaitingPayment, true))),
            Builders<Offer>.Update
                .Set(x => x.AwaitingPayment, false).Set(x => x.IsActive, false)
                .Set(x => x.IsDeleted, true).Set(x => x.DeletedAt, now).Set(x => x.DeleteReason, UnpaidReason),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Offer>> ListDonatedByAsync(Guid donorUserId, int limit, CancellationToken cancellationToken)
    {
        var filter = Active(Filter.And(Filter.Eq(x => x.DonorUserId, donorUserId), Filter.Ne(x => x.AwaitingPayment, true)));
        return await Collection.Find(filter).SortByDescending(x => x.CreatedAt).Limit(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> TakeAsync(FilterDefinition<Offer> filter, UpdateDefinition<Offer> update, CancellationToken cancellationToken)
    {
        var updated = await Collection.FindOneAndUpdateAsync(
            filter, update.Set(x => x.UpdatedAt, Clock.UtcNow), new FindOneAndUpdateOptions<Offer> { ReturnDocument = ReturnDocument.After },
            cancellationToken).ConfigureAwait(false);
        return updated is not null;
    }
}

public sealed class ClaimRepository(MongoContext context, IAggregateTracker tracker, IOutbox outbox, IClock clock)
    : SoftDeleteRepository<Claim, ClaimId>(context, tracker, outbox, clock, SocialCollectionNames.Claims), IClaimRepository
{
    public const string OneReservedIndex = "ux_claims_recipientUserId_reserved";

    public async Task<bool> TryAddReservedAsync(Claim claim, CancellationToken cancellationToken)
    {
        try
        {
            await AddAsync(claim, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey
            && ex.WriteError.Message.Contains(OneReservedIndex, StringComparison.Ordinal))
        {
            return false;
        }
        catch (MongoException ex) when (Context.Session is not null && ex.HasErrorLabel("TransientTransactionError"))
        {
            // Tranzaksiya daxilində: eyni istifadəçinin paralel tranzaksiyası eyni unikal açarı yazır (hələ commit olunmayıb).
            return false;
        }
    }

    public async Task<bool> TryTransitionAsync(Claim claim, ClaimStatus expected, CancellationToken cancellationToken)
    {
        try
        {
            return await TryReplaceAsync(claim, Filter.Eq(x => x.Status, expected), cancellationToken).ConfigureAwait(false);
        }
        catch (MongoException ex) when (Context.Session is not null && ex.HasErrorLabel("TransientTransactionError"))
        {
            // Paralel tranzaksiya eyni claim-i elə indi dəyişir (məs. iki işçi eyni kodu skan etdi): qalib o olur.
            return false;
        }
    }

    public async Task<Claim?> FindReservedForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var found = await FindAsync(
            Filter.And(Filter.Eq(x => x.RecipientUserId, userId), Filter.Eq(x => x.Status, ClaimStatus.Reserved)), cancellationToken).ConfigureAwait(false);
        return found.Count == 0 ? null : found[0];
    }

    public async Task<bool> TryCloseReservationAsync(Claim claim, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        var filter = Active(Filter.And(ById(claim.Id), Filter.Eq(x => x.Status, ClaimStatus.Reserved)));
        var result = await Collection.ReplaceOneAsync(filter, claim, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.MatchedCount == 1;
    }

    public async Task<long> CountCountedSinceAsync(Guid userId, DateTimeOffset since, OfferId? offerId, CancellationToken cancellationToken)
    {
        var filter = Filter.And(
            Filter.Eq(x => x.RecipientUserId, userId),
            Filter.In(x => x.Status, [ClaimStatus.Reserved, ClaimStatus.Redeemed]),
            Filter.Gte(x => x.CreatedAt, since));
        if (offerId is { } id)
        {
            filter = Filter.And(filter, Filter.Eq(x => x.OfferId, id));
        }

        return Context.Session is { } session
            ? await Collection.CountDocumentsAsync(session, Active(filter), cancellationToken: cancellationToken).ConfigureAwait(false)
            : await Collection.CountDocumentsAsync(Active(filter), cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> SumPoolSpendSinceAsync(Guid userId, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var filter = Active(Filter.And(
            Filter.Eq(x => x.RecipientUserId, userId),
            Filter.Eq(x => x.Source, ClaimSource.Pool),
            Filter.In(x => x.Status, [ClaimStatus.Reserved, ClaimStatus.Redeemed]),
            Filter.Gte(x => x.CreatedAt, since)));
        var claims = await Collection.Find(filter).ToListAsync(cancellationToken).ConfigureAwait(false);
        return claims.Sum(c => c.AmountQepik ?? 0);
    }

    public async Task<Claim?> FindByShortCodeAsync(BusinessId businessId, string shortCodeHash, CancellationToken cancellationToken)
    {
        var filter = Active(Filter.And(Filter.Eq(x => x.BusinessId, businessId), Filter.Eq(x => x.ShortCodeHash, shortCodeHash)));
        var find = Context.Session is { } session ? Collection.Find(session, filter) : Collection.Find(filter);
        var claim = await find.SortByDescending(x => x.CreatedAt).Limit(1).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (claim is not null)
        {
            Tracker.Track(claim);
        }

        return claim;
    }

    public Task<long> CountRedeemedForOfferAsync(OfferId offerId, CancellationToken cancellationToken) =>
        Collection.CountDocumentsAsync(
            Active(Filter.And(Filter.Eq(x => x.OfferId, offerId), Filter.Eq(x => x.Status, ClaimStatus.Redeemed))), cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<Claim>> ListReservedForBusinessAsync(BusinessId businessId, CancellationToken cancellationToken) =>
        await Collection.Find(Active(Filter.And(Filter.Eq(x => x.BusinessId, businessId), Filter.Eq(x => x.Status, ClaimStatus.Reserved))))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Claim>> ListReservedForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await Collection.Find(Active(Filter.And(Filter.Eq(x => x.RecipientUserId, userId), Filter.Eq(x => x.Status, ClaimStatus.Reserved))))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Claim>> ListStaleReservedAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken)
    {
        var filter = Active(Filter.And(Filter.Eq(x => x.Status, ClaimStatus.Reserved), Filter.Lte(x => x.ExpiresAt, now)));
        return await Collection.Find(filter).SortBy(x => x.ExpiresAt).Limit(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Claim>> ListForRecipientAsync(Guid userId, int limit, CancellationToken cancellationToken)
    {
        var filter = Active(Filter.Eq(x => x.RecipientUserId, userId));
        var find = Context.Session is { } session ? Collection.Find(session, filter) : Collection.Find(filter);
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
        services.AddScoped<IClaimRepository, ClaimRepository>();
        services.AddScoped<IPoolDonationRepository, PoolDonationRepository>();
        services.AddScoped<ICampaignRepository, CampaignRepository>();
        services.AddScoped<Application.Features.Donations.Commands.DonatePool.PoolDonationStarter>();
        services.AddScoped<OfferReferenceCheck>();
        services.AddScoped<SocialEligibility>();
        services.AddScoped<ClaimRelease>();
        services.AddScoped<SocialLimits>();
        services.AddScoped<ClaimPresenter>();
        services.AddScoped<RedemptionCodeResolver>();
        services.AddScoped<SocialCascade>();
        services.AddScoped<PoolAttribution>();
        services.AddScoped<Application.Features.Donations.Events.PaymentFulfillment>();
        return services;
    }
}
