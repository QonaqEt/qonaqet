using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Infrastructure.Persistence.Mongo.Migrations;

namespace Qonaqet.IntegrationTests.Persistence.Mongo;

/// <summary>Index runner və isDeleted backfill (E01-T07).</summary>
[Collection(MongoCollection.Name)]
public sealed class MongoMigrationTests(MongoFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Migration_creates_every_catalog_index_and_is_idempotent()
    {
        fixture.SkipIfUnavailable();
        var database = fixture.NewDatabase();
        var migrator = new MongoMigrator(database, new MongoIndexRunner(database, MongoTestScope.Logger<MongoIndexRunner>()), MongoTestScope.Logger<MongoMigrator>());

        var first = await migrator.RunAsync(Ct);
        var second = await migrator.RunAsync(Ct);

        Assert.Equal(MongoIndexCatalog.All.Count, first.Created.Count);
        Assert.Empty(second.Created);
        Assert.Empty(second.Dropped);
        Assert.Equal(MongoIndexCatalog.All.Count, second.Unchanged.Count);

        var users = await ListIndexesAsync(database, "users");
        var email = users.Single(i => i["name"] == "ux_users_email");
        Assert.True(email["unique"].ToBoolean());
        Assert.Equal(new BsonDocument("isDeleted", false), email["partialFilterExpression"].AsBsonDocument);
        Assert.False(users.Single(i => i["name"] == MongoIndexCatalog.AdminIndexName).Contains("partialFilterExpression"));
        Assert.All(users, i => Assert.False(i.Contains("expireAfterSeconds")));

        var geo = (await ListIndexesAsync(database, "businesses")).Single(i => i["name"] == "geo_businesses_location");
        Assert.Equal("2dsphere", geo["key"]["location"].AsString);
    }

    [Fact]
    public async Task Conflicting_old_non_partial_index_is_replaced_without_touching_documents()
    {
        fixture.SkipIfUnavailable();
        var database = fixture.NewDatabase();
        var users = database.GetCollection<BsonDocument>("users");
        await users.InsertOneAsync(new BsonDocument { { "email", "a@b.az" }, { "isDeleted", false } }, cancellationToken: Ct);
        await users.Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(new BsonDocument("email", 1), new CreateIndexOptions { Name = "email_1", Unique = true }),
            cancellationToken: Ct);
        var runner = new MongoIndexRunner(database, MongoTestScope.Logger<MongoIndexRunner>());

        var result = await runner.RunAsync(MongoIndexCatalog.All.Where(d => d.Collection == "users"), Ct);

        Assert.Contains("users.email_1", result.Dropped);
        var indexes = await ListIndexesAsync(database, "users");
        Assert.DoesNotContain(indexes, i => i["name"] == "email_1");
        Assert.Contains(indexes, i => i["name"] == "ux_users_email" && i.Contains("partialFilterExpression"));
        Assert.Equal(1, await users.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: Ct));
    }

    [Fact]
    public async Task Same_name_with_different_options_is_recreated()
    {
        fixture.SkipIfUnavailable();
        var database = fixture.NewDatabase();
        var gifts = database.GetCollection<BsonDocument>("gifts");
        await gifts.Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(new BsonDocument("linkTokenHash", 1), new CreateIndexOptions { Name = "ux_gifts_linkTokenHash" }),
            cancellationToken: Ct);
        var runner = new MongoIndexRunner(database, MongoTestScope.Logger<MongoIndexRunner>());

        var result = await runner.RunAsync(MongoIndexCatalog.All.Where(d => d.Collection == "gifts" && d.Name == "ux_gifts_linkTokenHash"), Ct);

        Assert.Equal(["gifts.ux_gifts_linkTokenHash"], result.Dropped);
        var index = (await ListIndexesAsync(database, "gifts")).Single(i => i["name"] == "ux_gifts_linkTokenHash");
        Assert.True(index["unique"].ToBoolean());
    }

    [Fact]
    public async Task Backfill_sets_missing_isDeleted_and_keeps_existing_values()
    {
        fixture.SkipIfUnavailable();
        var database = fixture.NewDatabase();
        var gifts = database.GetCollection<BsonDocument>("gifts");
        await gifts.InsertManyAsync(
        [
            new BsonDocument("_id", 1),
            new BsonDocument { { "_id", 2 }, { "isDeleted", true } },
            new BsonDocument { { "_id", 3 }, { "isDeleted", false } },
        ], cancellationToken: Ct);
        var migrator = new MongoMigrator(database, new MongoIndexRunner(database, MongoTestScope.Logger<MongoIndexRunner>()), MongoTestScope.Logger<MongoMigrator>());

        Assert.Equal(1, await migrator.BackfillIsDeletedAsync("gifts", Ct));
        Assert.Equal(0, await migrator.BackfillIsDeletedAsync("gifts", Ct));

        var docs = await gifts.Find(FilterDefinition<BsonDocument>.Empty).SortBy(d => d["_id"]).ToListAsync(Ct);
        Assert.Equal([false, true, false], docs.Select(d => d["isDeleted"].AsBoolean));
    }

    private static async Task<List<BsonDocument>> ListIndexesAsync(IMongoDatabase database, string collection)
    {
        using var cursor = await database.GetCollection<BsonDocument>(collection).Indexes.ListAsync(Ct);
        return await cursor.ToListAsync(Ct);
    }
}
