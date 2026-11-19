using System.Net;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.IntegrationTests.Identity;
using Qonaqet.IntegrationTests.Persistence.Mongo;
using static Qonaqet.IntegrationTests.Categories.CategoryTestData;

namespace Qonaqet.IntegrationTests.Categories;

/// <summary>Avtomatik default hovuz və silmə qoruması (E05-T02).</summary>
[Collection(MongoCollection.Name)]
public sealed class CategoryDeleteGuardTests(MongoFixture mongo)
{
    [Fact]
    public async Task Creating_a_category_creates_its_empty_default_pool()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var admin = await RolesTests.LoginAsAdminAsync(host, "admin@example.az");

        var id = (await CreateAsync(host, admin, StudentBody())).GetProperty("id").GetString()!;

        var pool = await Pools(host).Find(new BsonDocument { { "categoryId", IdValue(id) }, { "isDeleted", false } })
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(BsonNull.Value, pool.GetValue("campaignId", BsonNull.Value));
        Assert.Equal(0, pool["balanceQepik"].AsInt64);
        Assert.Equal(0, pool["reservedQepik"].AsInt64);
    }

    [Fact]
    public async Task Unused_category_is_soft_deleted_with_its_pool_and_can_be_restored()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var admin = await RolesTests.LoginAsAdminAsync(host, "admin@example.az");
        var id = (await CreateAsync(host, admin, StudentBody())).GetProperty("id").GetString()!;

        // Müddəti bitmiş təsdiq silməyə mane olmur.
        await ExpiredVerificationAsync(host, id);

        using (var delete = await host.SendAsync(HttpMethod.Delete, $"/api/v1/admin/categories/{id}?reason=lazım deyil", Access(admin)))
        {
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        Assert.DoesNotContain("test-students", await PublicCodesAsync(host));
        var stored = await host.Database.GetCollection<BsonDocument>("categories").Find(new BsonDocument("_id", IdValue(id))).SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(stored["isDeleted"].AsBoolean);
        Assert.Equal("lazım deyil", stored["deleteReason"].AsString);
        var pool = await Pools(host).Find(new BsonDocument("categoryId", IdValue(id))).SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(pool["isDeleted"].AsBoolean);
        Assert.Equal("category_deleted", pool["deleteReason"].AsString);

        using (var deleted = await host.GetAsync("/api/v1/admin/categories?includeDeleted=true", Access(admin)))
        {
            var item = (await host.JsonAsync(deleted)).GetProperty("items").EnumerateArray().Single(c => c.GetProperty("id").GetString() == id);
            Assert.True(item.GetProperty("isDeleted").GetBoolean());
        }

        using (var restore = await host.SendAsync(HttpMethod.Post, $"/api/v1/admin/categories/{id}/restore", Access(admin)))
        {
            Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);
        }

        Assert.Contains("test-students", await PublicCodesAsync(host));
        Assert.Equal(1, await Pools(host).CountDocumentsAsync(
            new BsonDocument { { "categoryId", IdValue(id) }, { "isDeleted", false } }, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Restore_is_refused_when_the_code_was_reused()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var admin = await RolesTests.LoginAsAdminAsync(host, "admin@example.az");
        var id = (await CreateAsync(host, admin, StudentBody())).GetProperty("id").GetString()!;
        using (var delete = await host.SendAsync(HttpMethod.Delete, $"/api/v1/admin/categories/{id}?reason=köhnə", Access(admin)))
        {
            delete.EnsureSuccessStatusCode();
        }

        await CreateAsync(host, admin, StudentBody());

        using var restore = await host.SendAsync(HttpMethod.Post, $"/api/v1/admin/categories/{id}/restore", Access(admin));
        Assert.Equal("category_code_taken", (await host.JsonAsync(restore)).GetProperty("code").GetString());
    }

    public static TheoryData<string> Blockers => ["active_offers", "active_campaigns", "pool_balance", "valid_verifications"];

    [Theory]
    [MemberData(nameof(Blockers))]
    public async Task Category_in_use_can_only_be_deactivated(string blocker)
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var admin = await RolesTests.LoginAsAdminAsync(host, "admin@example.az");
        var id = (await CreateAsync(host, admin, StudentBody())).GetProperty("id").GetString()!;

        switch (blocker)
        {
            case "active_offers":
                await InsertReferenceAsync(host, "offers", id, new BsonDocument { { "type", "BusinessDaily" }, { "isActive", true } });
                break;
            case "active_campaigns":
                await InsertReferenceAsync(host, "campaigns", id, new BsonDocument("status", "Active"));
                break;
            case "pool_balance":
                await Pools(host).UpdateOneAsync(
                    new BsonDocument("categoryId", IdValue(id)), new BsonDocument("$set", new BsonDocument("balanceQepik", 1500L)),
                    cancellationToken: TestContext.Current.CancellationToken);
                break;
            default:
                await VerifiedUserAsync(host, id, DateTime.UtcNow.AddMonths(6));
                break;
        }

        using (var delete = await host.SendAsync(HttpMethod.Delete, $"/api/v1/admin/categories/{id}?reason=sınaq", Access(admin)))
        {
            Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
            var problem = await host.JsonAsync(delete);
            Assert.Equal("category_in_use", problem.GetProperty("code").GetString());
            Assert.Contains(blocker, problem.GetProperty("title").GetString(), StringComparison.Ordinal);
        }

        using (var deactivate = await host.SendAsync(HttpMethod.Put, $"/api/v1/admin/categories/{id}", Access(admin), StudentBody(isActive: false)))
        {
            Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        }

        Assert.DoesNotContain("test-students", await PublicCodesAsync(host));
        var stored = await host.Database.GetCollection<BsonDocument>("categories").Find(new BsonDocument("_id", IdValue(id))).SingleAsync(TestContext.Current.CancellationToken);
        Assert.False(stored["isDeleted"].AsBoolean);
    }

    private static IMongoCollection<BsonDocument> Pools(ApiTestHost host) => host.Database.GetCollection<BsonDocument>("pools");

    private static Task ExpiredVerificationAsync(ApiTestHost host, string categoryId) => VerifiedUserAsync(host, categoryId, DateTime.UtcNow.AddDays(-1));

    private static Task VerifiedUserAsync(ApiTestHost host, string categoryId, DateTime validUntil) =>
        host.Database.GetCollection<BsonDocument>("users").InsertOneAsync(
            new BsonDocument
            {
                { "_id", new BsonBinaryData(Guid.CreateVersion7(), GuidRepresentation.Standard) },
                { "email", $"{Guid.NewGuid():N}@example.az" },
                { "isDeleted", false },
                { "verifiedCategories", new BsonArray { new BsonDocument { { "categoryId", IdValue(categoryId) }, { "validUntil", validUntil } } } },
            },
            cancellationToken: TestContext.Current.CancellationToken);
}
