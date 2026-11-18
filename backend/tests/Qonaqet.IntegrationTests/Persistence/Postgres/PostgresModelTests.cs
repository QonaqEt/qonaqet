using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Qonaqet.Infrastructure.Persistence.Postgres;

namespace Qonaqet.IntegrationTests.Persistence.Postgres;

/// <summary>EF modelinin konvensiyaları (verilənlər bazası tələb etmir).</summary>
public sealed class PostgresModelTests
{
    private static IModel Model()
    {
        using var context = PostgresFixture.CreateContext("Host=unused", o => new TestDbContext(o), PostgresFixture.Clock());
        return context.Model;
    }

    [Theory]
    [InlineData("CreatedAt", "created_at")]
    [InlineData("AmountQepik", "amount_qepik")]
    [InlineData("IX_AuditLogs_EntityId", "ix_audit_logs_entity_id")]
    [InlineData("PK_test_docs", "pk_test_docs")]
    [InlineData("Id", "id")]
    public void Snake_case_conversion(string input, string expected) => Assert.Equal(expected, ModelConventions.ToSnakeCase(input));

    [Fact]
    public void Every_table_is_in_the_qonaqet_schema_and_every_name_is_snake_case()
    {
        foreach (var entity in Model().GetEntityTypes())
        {
            Assert.Equal(QonaqetDbContext.Schema, entity.GetSchema());
            Assert.Equal(ModelConventions.ToSnakeCase(entity.GetTableName()!), entity.GetTableName());
            Assert.All(entity.GetProperties(), p => Assert.Equal(p.GetColumnName().ToLowerInvariant(), p.GetColumnName()));
            Assert.All(entity.GetIndexes(), i => Assert.Equal(i.GetDatabaseName()!.ToLowerInvariant(), i.GetDatabaseName()));
        }
    }

    [Fact]
    public void Money_is_bigint_and_times_are_timestamptz()
    {
        var doc = Model().FindEntityType(typeof(TestDoc))!;

        Assert.Equal("bigint", doc.FindProperty(nameof(TestDoc.AmountQepik))!.GetColumnType());
        Assert.Equal("timestamp with time zone", doc.FindProperty(nameof(TestDoc.CreatedAt))!.GetColumnType());
        Assert.Equal("uuid", doc.FindProperty(nameof(TestDoc.Id))!.GetColumnType());
    }

    [Fact]
    public void Soft_deletable_entities_get_a_query_filter_and_partial_indexes()
    {
        var model = Model();
        var doc = model.FindEntityType(typeof(TestDoc))!;

        Assert.NotNull(doc.GetDeclaredQueryFilters().SingleOrDefault());
        Assert.All(doc.GetIndexes(), i => Assert.Equal(ModelConventions.NotDeletedFilter, i.GetFilter()));

        // Append-only cədvəllərdə filtr yoxdur və indekslər adi qalır.
        var audit = model.FindEntityType(typeof(Infrastructure.Persistence.Postgres.Records.AuditLogRecord))!;
        Assert.Empty(audit.GetDeclaredQueryFilters());
        Assert.All(audit.GetIndexes(), i => Assert.Null(i.GetFilter()));
        Assert.Empty(model.FindEntityType(typeof(TestEntry))!.GetDeclaredQueryFilters());
    }
}
