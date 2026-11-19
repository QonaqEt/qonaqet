using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Domain.Users;
using Qonaqet.Infrastructure.Persistence;
using Qonaqet.Infrastructure.Persistence.Mongo.Migrations;
using Qonaqet.Infrastructure.Persistence.Mongo.Repositories;
using Qonaqet.IntegrationTests.Persistence.Mongo;

namespace Qonaqet.IntegrationTests.Identity;

/// <summary><c>users.email</c> partial unique index və silinmiş hesabın email-inin təkrar istifadəsi (E02-T07).</summary>
[Collection(MongoCollection.Name)]
public sealed class EmailReuseTests(MongoFixture mongo)
{
    private const string Email = "reuse@example.az";

    [Fact]
    public async Task Email_index_is_unique_only_among_active_users()
    {
        mongo.SkipIfUnavailable();
        var scope = new MongoTestScope(mongo);
        await new MongoIndexRunner(scope.Database, MongoTestScope.Logger<MongoIndexRunner>()).RunAsync(MongoIndexCatalog.All.Where(d => d.Collection == "users"), TestContext.Current.CancellationToken);

        var indexes = await (await scope.Database.GetCollection<BsonDocument>("users").Indexes.ListAsync(TestContext.Current.CancellationToken))
            .ToListAsync(TestContext.Current.CancellationToken);
        var email = indexes.Single(i => i["key"].AsBsonDocument.Names.SequenceEqual(["email"]));

        Assert.True(email["unique"].AsBoolean);
        Assert.Equal(new BsonDocument("isDeleted", false), email["partialFilterExpression"].AsBsonDocument);
    }

    [Fact]
    public async Task Second_active_account_with_the_same_email_is_rejected_but_a_deleted_one_frees_the_email()
    {
        mongo.SkipIfUnavailable();
        var scope = new MongoTestScope(mongo);
        await new MongoIndexRunner(scope.Database, MongoTestScope.Logger<MongoIndexRunner>()).RunAsync(MongoIndexCatalog.All.Where(d => d.Collection == "users"), TestContext.Current.CancellationToken);
        var users = new UserRepository(scope.Context, new AggregateTracker(), scope.Outbox, scope.Clock);
        var first = User.Register(Email, scope.Clock.UtcNow);
        await users.AddAsync(first, TestContext.Current.CancellationToken);

        var duplicate = await Assert.ThrowsAsync<MongoWriteException>(() => users.AddAsync(User.Register(Email.ToUpperInvariant(), scope.Clock.UtcNow), TestContext.Current.CancellationToken));
        Assert.Equal(ServerErrorCategory.DuplicateKey, duplicate.WriteError.Category);

        Assert.True(await users.SoftDeleteAsync(first.Id, first.Id.Value, "test", TestContext.Current.CancellationToken));
        var second = User.Register(Email, scope.Clock.UtcNow);
        await users.AddAsync(second, TestContext.Current.CancellationToken);

        Assert.Equal(second.Id, (await users.GetByEmailAsync(Email, TestContext.Current.CancellationToken))!.Id);
        Assert.Equal(2, await scope.Database.GetCollection<BsonDocument>("users").CountDocumentsAsync(new BsonDocument("email", Email), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task After_deleting_the_account_the_same_email_registers_a_new_account()
    {
        mongo.SkipIfUnavailable();
        await using var host = new ApiTestHost(mongo);
        var old = await host.LoginAsync(Email);
        using (var delete = await host.SendAsync(HttpMethod.Delete, "/api/v1/me", RolesTests.Access(old)))
        {
            delete.EnsureSuccessStatusCode();
        }

        var fresh = await host.LoginAsync(Email);

        Assert.True(fresh.GetProperty("isNewUser").GetBoolean());
        Assert.NotEqual(old.GetProperty("userId").GetString(), fresh.GetProperty("userId").GetString());
        var all = await host.Database.GetCollection<BsonDocument>("users").Find(new BsonDocument("email", Email)).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, all.Count);
        Assert.Single(all, u => u["isDeleted"].AsBoolean);
    }
}
