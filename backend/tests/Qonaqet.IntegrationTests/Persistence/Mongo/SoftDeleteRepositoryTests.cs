using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Infrastructure.Messaging.Outbox;
using Qonaqet.Infrastructure.Persistence.Mongo;

namespace Qonaqet.IntegrationTests.Persistence.Mongo;

/// <summary>Soft-delete repository real Mongo replica set-ə qarşı (E01-T07).</summary>
[Collection(MongoCollection.Name)]
public sealed class SoftDeleteRepositoryTests(MongoFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Stores_camelCase_fields_and_reads_aggregate_back()
    {
        fixture.SkipIfUnavailable();
        var scope = new MongoTestScope(fixture);
        var widget = new TestWidget("çay", scope.Clock.UtcNow);

        await scope.Repository.AddAsync(widget, Ct);

        var raw = await scope.Database.GetCollection<BsonDocument>(TestWidgetRepository.CollectionNameValue).Find(FilterDefinition<BsonDocument>.Empty).SingleAsync(Ct);
        Assert.False(raw["isDeleted"].AsBoolean);
        Assert.Equal("çay", raw["name"].AsString);
        Assert.Equal(BsonType.DateTime, raw["createdAt"].BsonType);
        Assert.Equal(widget.Id.Value, raw["_id"].AsGuid);

        var back = await scope.Repository.GetAsync(widget.Id, Ct);
        Assert.NotNull(back);
        Assert.Equal("çay", back.Name);
        Assert.Equal(widget.CreatedAt, back.CreatedAt);
    }

    [Fact]
    public async Task Soft_deleted_records_are_hidden_from_every_query_and_visible_only_via_IncludeDeleted()
    {
        fixture.SkipIfUnavailable();
        var scope = new MongoTestScope(fixture);
        await scope.EnsureCollectionsAsync();
        var keep = new TestWidget("a", scope.Clock.UtcNow);
        var gone = new TestWidget("a", scope.Clock.UtcNow);
        await scope.Repository.AddAsync(keep, Ct);
        await scope.Repository.AddAsync(gone, Ct);
        var userId = Guid.CreateVersion7();

        Assert.True(await scope.Repository.SoftDeleteAsync(gone.Id, userId, "test", Ct));
        Assert.False(await scope.Repository.SoftDeleteAsync(gone.Id, userId, "again", Ct));

        Assert.Null(await scope.Repository.GetAsync(gone.Id, Ct));
        Assert.Equal([keep.Id], (await scope.Repository.FindByNameAsync("a", Ct)).Select(w => w.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Repository.UpdateAsync(gone, Ct));

        var all = await scope.Repository.IncludeDeleted().FindAsync(FilterDefinition<TestWidget>.Empty, Ct);
        Assert.Equal(2, all.Count);
        var deleted = Assert.Single(all, w => w.IsDeleted);
        Assert.Equal(userId, deleted.DeletedBy);
        Assert.Equal("test", deleted.DeleteReason);
        Assert.Equal(scope.Clock.UtcNow, deleted.DeletedAt);

        // Fiziki silmə yoxdur: sənəd kolleksiyada qalır.
        Assert.Equal(2, await scope.Database.GetCollection<BsonDocument>(TestWidgetRepository.CollectionNameValue).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: Ct));
    }

    [Fact]
    public async Task Soft_delete_and_restore_write_audit_entries_to_the_outbox()
    {
        fixture.SkipIfUnavailable();
        var scope = new MongoTestScope(fixture);
        await scope.EnsureCollectionsAsync();
        var widget = new TestWidget("b", scope.Clock.UtcNow);
        await scope.Repository.AddAsync(widget, Ct);
        var userId = Guid.CreateVersion7();

        await scope.Repository.SoftDeleteAsync(widget.Id, userId, "səhv", Ct);
        Assert.True(await scope.Repository.RestoreAsync(widget.Id, userId, Ct));
        Assert.False(await scope.Repository.RestoreAsync(widget.Id, userId, Ct));

        var restored = await scope.Repository.GetAsync(widget.Id, Ct);
        Assert.NotNull(restored);
        Assert.False(restored.IsDeleted);
        Assert.Null(restored.DeleteReason);

        var messages = await scope.Database.GetCollection<OutboxMessage>(MongoOutbox.CollectionName)
            .Find(FilterDefinition<OutboxMessage>.Empty).SortBy(m => m.Id).ToListAsync(Ct);
        Assert.Equal(2, messages.Count);
        Assert.All(messages, m => Assert.Equal(typeof(AuditRecorded).FullName, m.Type));
        Assert.All(messages, m => Assert.Null(m.ProcessedAt));

        var actions = messages.Select(m => JsonDocument.Parse(m.Payload).RootElement).ToArray();
        Assert.Equal([AuditActions.SoftDelete, AuditActions.Restore], actions.Select(a => a.GetProperty("action").GetString()));
        Assert.All(actions, a => Assert.Equal(widget.Id.Value, a.GetProperty("entityId").GetGuid()));
        Assert.All(actions, a => Assert.Equal(userId, a.GetProperty("actorId").GetGuid()));
        Assert.Equal("səhv", actions[0].GetProperty("data").GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Rolled_back_unit_of_work_leaves_neither_the_change_nor_the_audit_entry()
    {
        fixture.SkipIfUnavailable();
        var scope = new MongoTestScope(fixture);
        await scope.EnsureCollectionsAsync();
        var widget = new TestWidget("c", scope.Clock.UtcNow);
        await scope.Repository.AddAsync(widget, Ct);

        await scope.UnitOfWork.BeginAsync(Ct);
        Assert.True(await scope.Repository.SoftDeleteAsync(widget.Id, null, null, Ct));
        await scope.UnitOfWork.RollbackAsync(Ct);

        Assert.NotNull(await scope.Repository.GetAsync(widget.Id, Ct));
        Assert.Equal(0, await scope.Database.GetCollection<OutboxMessage>(MongoOutbox.CollectionName).CountDocumentsAsync(FilterDefinition<OutboxMessage>.Empty, cancellationToken: Ct));
    }

    [Fact]
    public async Task Committed_unit_of_work_persists_update_and_outbox_together()
    {
        fixture.SkipIfUnavailable();
        var scope = new MongoTestScope(fixture);
        await scope.EnsureCollectionsAsync();
        var widget = new TestWidget("d", scope.Clock.UtcNow);
        await scope.Repository.AddAsync(widget, Ct);

        await scope.UnitOfWork.BeginAsync(Ct);
        widget.Rename("e", scope.Clock.UtcNow);
        await scope.Repository.UpdateAsync(widget, Ct);
        await scope.Repository.SoftDeleteAsync(widget.Id, null, null, Ct);
        await scope.UnitOfWork.CommitAsync(Ct);

        var stored = Assert.Single(await scope.Repository.IncludeDeleted().FindAsync(FilterDefinition<TestWidget>.Empty, Ct));
        Assert.Equal("e", stored.Name);
        Assert.True(stored.IsDeleted);
        Assert.Equal(1, await scope.Database.GetCollection<OutboxMessage>(MongoOutbox.CollectionName).CountDocumentsAsync(FilterDefinition<OutboxMessage>.Empty, cancellationToken: Ct));
    }

    [Fact]
    public void Active_filter_always_carries_isDeleted_false()
    {
        var rendered = SoftDeleteRepository<TestWidget, TestWidgetId>
            .Active(MongoDB.Driver.Builders<TestWidget>.Filter.Eq(x => x.Name, "x"))
            .Render(new RenderArgs<TestWidget>(MongoDB.Bson.Serialization.BsonSerializer.LookupSerializer<TestWidget>(), MongoDB.Bson.Serialization.BsonSerializer.SerializerRegistry));

        Assert.Equal(BsonBoolean.False, rendered["isDeleted"]);
        Assert.Equal("x", rendered["name"].AsString);
    }
}
