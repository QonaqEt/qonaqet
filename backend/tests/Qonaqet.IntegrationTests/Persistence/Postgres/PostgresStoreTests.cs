using Microsoft.EntityFrameworkCore;
using Npgsql;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Infrastructure.Persistence.Postgres;

namespace Qonaqet.IntegrationTests.Persistence.Postgres;

/// <summary>Migrasiya, interceptor-lar və idempotency store real Postgres-ə qarşı (E01-T08).</summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresStoreTests(PostgresFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Migration_matches_the_ef_model_column_by_column()
    {
        fixture.SkipIfUnavailable();
        var connection = await fixture.NewDatabaseAsync();
        await using var context = PostgresFixture.CreateContext(connection, o => new QonaqetDbContext(o), PostgresFixture.Clock());

        await context.Database.MigrateAsync(Ct);
        await context.Database.MigrateAsync(Ct); // idempotent

        var columns = await QueryAsync(connection,
            "SELECT table_name, column_name, data_type, character_maximum_length, is_nullable, udt_name FROM information_schema.columns WHERE table_schema = 'qonaqet' AND table_name <> '__ef_migrations_history'",
            r => $"{r.GetString(0)}.{r.GetString(1)}:{Normalize(r.GetString(2), r.IsDBNull(3) ? null : r.GetInt32(3), r.GetString(5))}:{r.GetString(4)}");

        var expected = context.Model.GetEntityTypes().SelectMany(e => e.GetProperties().Select(p =>
            $"{e.GetTableName()}.{p.GetColumnName()}:{p.GetColumnType()}:{(p.IsNullable ? "YES" : "NO")}"));

        Assert.Equal(expected.Order(StringComparer.Ordinal), columns.Order(StringComparer.Ordinal));

        var indexes = await QueryAsync(connection,
            "SELECT indexname, indexdef FROM pg_indexes WHERE schemaname = 'qonaqet' AND indexname NOT ILIKE 'pk%'",
            r => r.GetString(0));
        var expectedIndexes = context.Model.GetEntityTypes().SelectMany(e => e.GetIndexes()).Select(i => i.GetDatabaseName()!);
        Assert.Equal(expectedIndexes.Order(StringComparer.Ordinal), indexes.Where(i => !i.StartsWith("__", StringComparison.Ordinal)).Order(StringComparer.Ordinal));

        var outboxIndex = (await QueryAsync(connection, "SELECT indexdef FROM pg_indexes WHERE indexname = 'ix_outbox_messages_processed_at'", r => r.GetString(0))).Single();
        Assert.Contains("WHERE (processed_at IS NULL)", outboxIndex, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Remove_becomes_a_soft_delete_hidden_by_the_global_filter_and_audited()
    {
        fixture.SkipIfUnavailable();
        var connection = await fixture.NewDatabaseAsync();
        var clock = PostgresFixture.Clock();
        var userId = Guid.CreateVersion7();
        TestDoc doc;
        await using (var context = await CreateAsync(connection, clock, userId))
        {
            doc = new TestDoc("çay", 150, clock.UtcNow);
            context.Docs.Add(doc);
            await context.SaveChangesAsync(Ct);

            context.Docs.Remove(doc);
            await context.SaveChangesAsync(Ct);
        }

        await using var read = PostgresFixture.CreateContext(connection, o => new TestDbContext(o), clock);
        Assert.Empty(await read.Docs.ToListAsync(Ct));
        var stored = await read.Docs.IgnoreQueryFilters().SingleAsync(Ct);
        Assert.True(stored.IsDeleted);
        Assert.Equal(userId, stored.DeletedBy);
        Assert.Equal(clock.UtcNow, stored.DeletedAt);

        var audit = await read.AuditLogs.OrderBy(a => a.Id).ToListAsync(Ct);
        Assert.Equal(["Create", "SoftDelete"], audit.Select(a => a.Action));
        Assert.All(audit, a => Assert.Equal(doc.Id.Value, a.EntityId));
        Assert.All(audit, a => Assert.Equal(nameof(TestDoc), a.EntityType));
        Assert.All(audit, a => Assert.Equal(userId, a.ActorId));
        Assert.DoesNotContain("çay", string.Concat(audit.Select(a => a.Data)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_append_only_rows_throws_and_writes_nothing()
    {
        fixture.SkipIfUnavailable();
        var connection = await fixture.NewDatabaseAsync();
        await using var context = await CreateAsync(connection, PostgresFixture.Clock(), null);
        var entry = new TestEntry { Id = Guid.CreateVersion7(), Amount = 500 };
        context.Entries.Add(entry);
        await context.SaveChangesAsync(Ct);

        context.Entries.Remove(entry);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(Ct));

        await using var read = PostgresFixture.CreateContext(connection, o => new TestDbContext(o), PostgresFixture.Clock());
        Assert.Equal(1, await read.Entries.CountAsync(Ct));
    }

    [Fact]
    public async Task Domain_events_reach_the_outbox_in_the_same_transaction()
    {
        fixture.SkipIfUnavailable();
        var connection = await fixture.NewDatabaseAsync();
        var clock = PostgresFixture.Clock();
        await using (var context = await CreateAsync(connection, clock, null))
        {
            var unitOfWork = new PostgresUnitOfWork(context);

            await unitOfWork.BeginAsync(Ct);
            var committed = new TestDoc("a", 1, clock.UtcNow);
            committed.Rename("b", clock.UtcNow);
            context.Docs.Add(committed);
            await unitOfWork.CommitAsync(Ct);

            await unitOfWork.BeginAsync(Ct);
            var rolledBack = new TestDoc("c", 1, clock.UtcNow);
            rolledBack.Rename("d", clock.UtcNow);
            context.Docs.Add(rolledBack);
            await context.SaveChangesAsync(Ct);
            await unitOfWork.RollbackAsync(Ct);
        }

        await using var read = PostgresFixture.CreateContext(connection, o => new TestDbContext(o), clock);
        var message = Assert.Single(await read.OutboxMessages.ToListAsync(Ct));
        Assert.Equal(typeof(TestDocRenamed).FullName, message.Type);
        Assert.Contains("\"name\":\"b\"", message.Payload.Replace(" ", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Null(message.ProcessedAt);
        Assert.Equal(["b"], await read.Docs.Select(d => d.Name).ToListAsync(Ct));
    }

    [Fact]
    public async Task Idempotency_store_keeps_the_first_record_for_a_key()
    {
        fixture.SkipIfUnavailable();
        var connection = await fixture.NewDatabaseAsync();
        var clock = PostgresFixture.Clock();
        await using var context = await CreateAsync(connection, clock, null);
        var store = new PostgresIdempotencyStore(context);

        Assert.Null(await store.FindAsync("scope", "k1", Ct));
        var first = await store.AddAsync(new IdempotencyRecord("scope", "k1", "h1", "{\"id\":1}", clock.UtcNow), Ct);
        var second = await store.AddAsync(new IdempotencyRecord("scope", "k1", "h2", "{\"id\":2}", clock.UtcNow), Ct);

        Assert.Equal("h1", first.RequestHash);
        Assert.Equal("h1", second.RequestHash);
        Assert.Equal("h1", (await store.FindAsync("scope", "k1", Ct))!.RequestHash);
        Assert.Equal(1, await context.IdempotencyRecords.CountAsync(Ct));
    }

    private static async Task<TestDbContext> CreateAsync(string connection, Mongo.FixedClock clock, Guid? userId)
    {
        var context = PostgresFixture.CreateContext(connection, o => new TestDbContext(o), clock, userId is { } id ? new TestUser(id) : null);
        await context.Database.EnsureCreatedAsync(Ct);
        return context;
    }

    private static string Normalize(string dataType, int? length, string udtName) => dataType switch
    {
        "character varying" => $"character varying({length})",
        "ARRAY" => $"{udtName.TrimStart('_')}[]", // _uuid → uuid[], _text → text[]
        _ => dataType,
    };

    private static async Task<List<string>> QueryAsync(string connection, string sql, Func<NpgsqlDataReader, string> map)
    {
        await using var conn = new NpgsqlConnection(connection);
        await conn.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, conn);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var result = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            result.Add(map(reader));
        }

        return result;
    }
}
