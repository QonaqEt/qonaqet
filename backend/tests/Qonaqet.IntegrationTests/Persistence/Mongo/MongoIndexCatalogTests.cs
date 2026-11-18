using Qonaqet.Infrastructure.Persistence.Mongo.Migrations;

namespace Qonaqet.IntegrationTests.Persistence.Mongo;

/// <summary>İndeks kataloqu <c>CLAUDE.md</c> → "Indexing rules" ilə uyğundur (Docker tələb etmir).</summary>
public sealed class MongoIndexCatalogTests
{
    [Fact]
    public void Index_names_are_unique_per_collection()
    {
        var duplicates = MongoIndexCatalog.All.GroupBy(d => (d.Collection, d.Name)).Where(g => g.Count() > 1).Select(g => g.Key);

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Every_unique_and_query_index_on_soft_deletable_data_is_partial()
    {
        var nonPartial = MongoIndexCatalog.All
            .Where(d => !d.Partial && d.Name != MongoIndexCatalog.AdminIndexName && d.Collection != "outboxMessages")
            .Select(d => $"{d.Collection}.{d.Name}");

        Assert.Empty(nonPartial);
        Assert.All(MongoIndexCatalog.All.Where(d => d.Unique), d => Assert.True(d.Partial));
    }

    [Fact]
    public void Every_soft_deletable_collection_has_an_admin_include_deleted_index()
    {
        var admin = MongoIndexCatalog.All.Where(d => d.Name == MongoIndexCatalog.AdminIndexName).Select(d => d.Collection).ToHashSet();

        Assert.True(admin.SetEquals(MongoIndexCatalog.SoftDeletableCollections));
    }

    [Theory]
    [InlineData("users", "ux_users_email")]
    [InlineData("categories", "ux_categories_code")]
    [InlineData("identityHashes", "ux_identityHashes_finHash_categoryId")]
    [InlineData("businesses", "ux_businesses_voen")]
    [InlineData("businesses", "ux_businesses_slug")]
    [InlineData("couponCodes", "ux_couponCodes_couponId_code")]
    [InlineData("gifts", "ux_gifts_linkTokenHash")]
    public void Required_unique_indexes_exist(string collection, string name)
    {
        Assert.Contains(MongoIndexCatalog.All, d => d.Collection == collection && d.Name == name && d.Unique);
    }
}
