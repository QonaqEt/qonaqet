using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Plus.Commands.AdminCoupons;
using Qonaqet.Domain.Plus;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Persistence.Mongo.Repositories;

public sealed class BrandRepository(MongoContext context, IAggregateTracker tracker, IOutbox outbox, IClock clock)
    : SoftDeleteRepository<Brand, BrandId>(context, tracker, outbox, clock, PlusCollectionNames.Brands), IBrandRepository
{
    public async Task<IReadOnlyList<Brand>> ListAsync(int limit, CancellationToken cancellationToken) =>
        await Collection.Find(Active()).SortBy(x => x.Name).Limit(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
}

public sealed class CouponRepository(MongoContext context, IAggregateTracker tracker, IOutbox outbox, IClock clock)
    : SoftDeleteRepository<Coupon, CouponId>(context, tracker, outbox, clock, PlusCollectionNames.Coupons), ICouponRepository
{
    public async Task<IReadOnlyList<Coupon>> ListAsync(BrandId? brandId, int limit, CancellationToken cancellationToken)
    {
        var filter = brandId is { } id ? Active(Filter.Eq(x => x.BrandId, id)) : Active();
        return await Collection.Find(filter).SortByDescending(x => x.CreatedAt).Limit(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class CouponCodeRepository(MongoContext context, IAggregateTracker tracker, IOutbox outbox, IClock clock)
    : SoftDeleteRepository<CouponCode, CouponCodeId>(context, tracker, outbox, clock, PlusCollectionNames.CouponCodes), ICouponCodeRepository
{
    public async Task<CouponCodeImport> AddCodesAsync(IReadOnlyCollection<CouponCode> codes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(codes);
        if (codes.Count == 0)
        {
            return new CouponCodeImport(0, 0);
        }

        try
        {
            await Collection.InsertManyAsync(codes, new InsertManyOptions { IsOrdered = false }, cancellationToken).ConfigureAwait(false);
            return new CouponCodeImport(codes.Count, 0);
        }
        catch (MongoBulkWriteException<CouponCode> ex) when (ex.WriteErrors.All(e => e.Category == ServerErrorCategory.DuplicateKey))
        {
            return new CouponCodeImport(codes.Count - ex.WriteErrors.Count, ex.WriteErrors.Count);
        }
    }

    public async Task<CouponCodeCounts> CountsAsync(CouponId couponId, CancellationToken cancellationToken)
    {
        var ofCoupon = Filter.Eq(x => x.CouponId, couponId);
        var total = await Collection.CountDocumentsAsync(Active(ofCoupon), cancellationToken: cancellationToken).ConfigureAwait(false);
        var assigned = await Collection.CountDocumentsAsync(Active(Filter.And(ofCoupon, Filter.Ne(x => x.AssignedUserId, null))), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var used = await Collection.CountDocumentsAsync(Active(Filter.And(ofCoupon, Filter.Ne(x => x.UsedAt, null))), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return new CouponCodeCounts((int)total, (int)assigned, (int)used);
    }
}

public static class PlusCollectionNames
{
    public const string Brands = "brands";
    public const string Coupons = "coupons";
    public const string CouponCodes = "couponCodes";
}

/// <summary>QONAQET Plus sahəsinin DI qeydiyyatı.</summary>
public static class PlusRegistration
{
    public static IServiceCollection AddPlus(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IBrandRepository, BrandRepository>();
        services.AddScoped<ICouponRepository, CouponRepository>();
        services.AddScoped<ICouponCodeRepository, CouponCodeRepository>();
        services.AddScoped<CouponCategories>();
        return services;
    }
}
