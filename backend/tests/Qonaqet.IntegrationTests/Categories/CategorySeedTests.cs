using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Infrastructure.Persistence.Mongo.Seed;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Persistence.Mongo;
using static Qonaqet.IntegrationTests.Categories.CategoryTestData;

namespace Qonaqet.IntegrationTests.Categories;

/// <summary>İlkin kateqoriyalar və universitet siyahısı (E05-T03).</summary>
[Collection(MongoCollection.Name)]
public sealed class CategorySeedTests(MongoFixture mongo)
{
    [Fact]
    public async Task Startup_seeds_the_four_categories_exactly_as_specified()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);

        using var response = await host.GetAsync("/api/v1/categories", null);
        var categories = (await host.JsonAsync(response)).EnumerateArray().ToDictionary(c => c.GetProperty("code").GetString()!);
        Assert.Equal(["everyone", "student", "veteran-family", "in-need"], categories.Keys.ToArray());

        // code, isOpen, privacy, FIN, validity, perDay, perMonth, poolAccess, poolSpend/day
        Expect(categories["everyone"], isOpen: true, "Public", fin: false, "None", 1, 15, pool: false, 0);
        Expect(categories["student"], isOpen: false, "Public", fin: true, "FixedDate", 2, 40, pool: true, 500);
        Expect(categories["veteran-family"], isOpen: false, "Sensitive", fin: true, "Months", 3, 60, pool: true, 1500);
        Expect(categories["in-need"], isOpen: false, "Sensitive", fin: true, "Months", 3, 60, pool: true, 1500);

        var studentValidity = categories["student"].GetProperty("verification").GetProperty("validity");
        Assert.Equal(9, studentValidity.GetProperty("month").GetInt32());
        Assert.Equal(30, studentValidity.GetProperty("day").GetInt32());
        Assert.Equal(12, categories["veteran-family"].GetProperty("verification").GetProperty("validity").GetProperty("months").GetInt32());
        Assert.Equal(6, categories["in-need"].GetProperty("verification").GetProperty("validity").GetProperty("months").GetInt32());

        var subtypes = categories["veteran-family"].GetProperty("verification").GetProperty("subtypes").EnumerateArray()
            .Select(s => s.GetProperty("code").GetString()).ToArray();
        Assert.Equal(["qazi", "martyr-family"], subtypes);

        var university = categories["student"].GetProperty("verification").GetProperty("fields").EnumerateArray().Single();
        Assert.Equal("university", university.GetProperty("key").GetString());
        Assert.Equal("Select", university.GetProperty("type").GetString());
        Assert.True(university.GetProperty("required").GetBoolean());
        var options = university.GetProperty("options").EnumerateArray().Select(o => o.GetString()!).ToArray();
        Assert.Equal(CategorySeedData.Universities, options);
        Assert.Contains("Bakı Dövlət Universiteti", options);

        // Hər seed kateqoriyasının bir default hovuzu var.
        var pools = host.Database.GetCollection<BsonDocument>("pools");
        foreach (var category in categories.Values)
        {
            Assert.Equal(1, await pools.CountDocumentsAsync(
                new BsonDocument { { "categoryId", IdValue(category.GetProperty("id").GetString()!) }, { "campaignId", BsonNull.Value }, { "isDeleted", false } },
                cancellationToken: TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Seed_is_idempotent_and_respects_admin_changes()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var admin = await RolesTests.LoginAsAdminAsync(host, "admin@example.az");
        var categories = host.Database.GetCollection<BsonDocument>("categories");
        var everyone = await categories.Find(new BsonDocument("code", "everyone")).SingleAsync(TestContext.Current.CancellationToken);
        var everyoneId = everyone["_id"].AsGuid.ToString();

        // Admin "everyone"-u silir və "student"-in limitini dəyişir.
        using (var delete = await host.SendAsync(HttpMethod.Delete, $"/api/v1/admin/categories/{everyoneId}?reason=sınaq", Access(admin)))
        {
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        await categories.UpdateOneAsync(
            new BsonDocument("code", "student"), new BsonDocument("$set", new BsonDocument("limits.perMonth", 99)),
            cancellationToken: TestContext.Current.CancellationToken);

        var seeder = host.Services.GetRequiredService<CategorySeeder>();
        Assert.Equal(0, await seeder.RunAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await seeder.RunAsync(TestContext.Current.CancellationToken));

        Assert.Equal(4, await categories.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(4, await host.Database.GetCollection<BsonDocument>("pools").CountDocumentsAsync(
            FilterDefinition<BsonDocument>.Empty, cancellationToken: TestContext.Current.CancellationToken));
        var student = await categories.Find(new BsonDocument("code", "student")).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(99, student["limits"]["perMonth"].AsInt32);
        Assert.DoesNotContain("everyone", await PublicCodesAsync(host));
    }

    private static void Expect(
        System.Text.Json.JsonElement category, bool isOpen, string privacy, bool fin, string validity, int perDay, int perMonth, bool pool, long poolSpend)
    {
        Assert.Equal(isOpen, category.GetProperty("isOpen").GetBoolean());
        Assert.Equal(privacy, category.GetProperty("privacyLevel").GetString());
        var verification = category.GetProperty("verification");
        Assert.Equal(fin, verification.GetProperty("requiresFin").GetBoolean());
        Assert.Equal(validity, verification.GetProperty("validity").GetProperty("type").GetString());
        Assert.Equal(isOpen ? 0 : 1, verification.GetProperty("minFiles").GetInt32());
        var limits = category.GetProperty("limits");
        Assert.Equal(perDay, limits.GetProperty("perDay").GetInt32());
        Assert.Equal(perMonth, limits.GetProperty("perMonth").GetInt32());
        Assert.Equal(pool, limits.GetProperty("poolAccess").GetBoolean());
        Assert.Equal(poolSpend, limits.GetProperty("poolSpendPerDayQepik").GetInt64());
    }
}
