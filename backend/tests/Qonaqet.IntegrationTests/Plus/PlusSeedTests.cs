using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Infrastructure.Persistence.Mongo.Seed;
using Qonaqet.IntegrationTests.Payments;
using static Qonaqet.IntegrationTests.Payments.PaymentTestData;

namespace Qonaqet.IntegrationTests.Plus;

/// <summary>QONAQET Plus demo seed (E12-T04): 1 brend + 2 kupon (Shared və Unique), idempotent.</summary>
[Collection(PaymentsCollection.Name)]
public sealed class PlusSeedTests(PaymentsFixture fixture)
{
    [Fact]
    public async Task Demo_seed_creates_one_brand_and_a_shared_and_a_unique_coupon_once()
    {
        fixture.SkipIfUnavailable();
        await using var host = await fixture.HostAsync();
        var seeder = host.Services.GetRequiredService<PlusDemoSeeder>();

        await seeder.RunAsync(Ct);
        Assert.Equal(0, await seeder.RunAsync(Ct));

        var brandId = new BsonBinaryData(PlusSeedData.BrandId.Value, GuidRepresentation.Standard);
        Assert.Equal(1, await host.Database.GetCollection<BsonDocument>("brands").CountDocumentsAsync(new BsonDocument("_id", brandId), cancellationToken: Ct));
        var coupons = await host.Database.GetCollection<BsonDocument>("coupons").Find(new BsonDocument("brandId", brandId)).ToListAsync(Ct);
        Assert.Equal(["Shared", "Unique"], coupons.Select(c => c["codeType"].AsString).Order(StringComparer.Ordinal));
        Assert.Equal(PlusSeedData.SharedCode, coupons.Single(c => c["codeType"] == "Shared")["sharedCode"].AsString);

        var codes = await host.Database.GetCollection<BsonDocument>("couponCodes").CountDocumentsAsync(
            new BsonDocument("couponId", new BsonBinaryData(PlusSeedData.UniqueCouponId.Value, GuidRepresentation.Standard)), cancellationToken: Ct);
        Assert.Equal(PlusSeedData.UniqueCodeCount, codes);
    }
}
