using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Qonaqet.Domain.Plus;
using Qonaqet.Infrastructure.Persistence.Mongo.Repositories;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Persistence.Mongo.Seed;

/// <summary>Development demo datası (CLAUDE.md → Seed data): 1 QONAQET Plus brendi + 2 kupon (biri <c>Shared</c>, biri <c>Unique</c>).</summary>
public static class PlusSeedData
{
    // Sabit id-lər seed-i idempotent edir: sənəd (silinmiş də daxil) varsa toxunulmur.
    public static readonly BrandId BrandId = BrandId.From(Guid.Parse("0193f7a0-0000-7000-8000-00000000b001"));

    public static readonly CouponId SharedCouponId = CouponId.From(Guid.Parse("0193f7a0-0000-7000-8000-00000000c001"));

    public static readonly CouponId UniqueCouponId = CouponId.From(Guid.Parse("0193f7a0-0000-7000-8000-00000000c002"));

    public const string SharedCode = "QONAQET-PLUS";

    public const int UniqueCodeCount = 20;

    public static string UniqueCode(int index) => $"DEMO-{index:D4}";
}

/// <summary>
/// Idempotent demo seed (<c>Mongo:SeedDemoData</c>, yalnız Development compose-da açıqdır). Brend və kuponlar sabit id ilə yaradılır; artıq
/// varsa (admin dəyişib və ya silibsə də) toxunulmur. Unikal kuponun kodları təkrar işlədikdə unikal indeksdə atlanır.
/// </summary>
public sealed partial class PlusDemoSeeder(IMongoDatabase database, IClock clock, ILogger<PlusDemoSeeder> logger)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var created = 0;
        var brands = database.GetCollection<Brand>(PlusCollectionNames.Brands);
        var brand = Brand.Create(
            new BrandDetails("Nar Kitab Evi (demo)", null, "https://example.az", "plus@example.az", IsActive: true), now, by: null, PlusSeedData.BrandId);
        created += await InsertIfMissingAsync(brands, brand, Builders<Brand>.Filter.Eq(b => b.Id, PlusSeedData.BrandId), cancellationToken).ConfigureAwait(false);

        var coupons = database.GetCollection<Coupon>(PlusCollectionNames.Coupons);
        var validity = new CouponDetails(string.Empty, null, null, [], 1, now, now.AddYears(1), IsActive: true);
        var shared = Coupon.Create(
            PlusSeedData.BrandId, CouponCodeType.Shared, PlusSeedData.SharedCode,
            validity with { Title = "Bütün kitablara 10% endirim", Description = "Kassada kodu deyin." }, now, by: null, PlusSeedData.SharedCouponId);
        var unique = Coupon.Create(
            PlusSeedData.BrandId, CouponCodeType.Unique, null,
            validity with { Title = "İlk alışa 5 AZN endirim", Description = "Hər istifadəçiyə bir kod.", PerUserLimit = 1 }, now, by: null,
            PlusSeedData.UniqueCouponId);
        created += await InsertIfMissingAsync(coupons, shared, Builders<Coupon>.Filter.Eq(c => c.Id, PlusSeedData.SharedCouponId), cancellationToken).ConfigureAwait(false);
        created += await InsertIfMissingAsync(coupons, unique, Builders<Coupon>.Filter.Eq(c => c.Id, PlusSeedData.UniqueCouponId), cancellationToken).ConfigureAwait(false);

        var codes = database.GetCollection<CouponCode>(PlusCollectionNames.CouponCodes);
        var demoCodes = Enumerable.Range(1, PlusSeedData.UniqueCodeCount)
            .Select(i => CouponCode.Create(PlusSeedData.UniqueCouponId, PlusSeedData.UniqueCode(i), now, by: null)).ToList();
        try
        {
            await codes.InsertManyAsync(demoCodes, new InsertManyOptions { IsOrdered = false }, cancellationToken).ConfigureAwait(false);
        }
        catch (MongoBulkWriteException<CouponCode> ex) when (ex.WriteErrors.All(e => e.Category == ServerErrorCategory.DuplicateKey))
        {
            // Kodlar artıq var.
        }

        if (created > 0)
        {
            LogSeeded(created);
        }

        return created;
    }

    private static async Task<int> InsertIfMissingAsync<T>(IMongoCollection<T> collection, T document, FilterDefinition<T> byId, CancellationToken cancellationToken)
    {
        if (await collection.Find(byId).AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return 0;
        }

        try
        {
            await collection.InsertOneAsync(document, cancellationToken: cancellationToken).ConfigureAwait(false);
            return 1;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return 0; // başqa host eyni anda seed etdi
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "QONAQET Plus demo datası seed olundu: {Count} sənəd")]
    private partial void LogSeeded(int count);
}
