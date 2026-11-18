using Qonaqet.ArchitectureTests.Support;

namespace Qonaqet.ArchitectureTests;

/// <summary>
/// <c>src/</c>-də hard delete və kateqoriya koduna görə şaxələnmə qadağası (E01-T14).
/// Hər qayda əvvəlcə nümunə "pis" kodla sınanır ki, testin həqiqətən qırıldığı sübut olunsun.
/// </summary>
public sealed class SourceScanTests
{
    public static TheoryData<string, string> HardDeleteSamples => new()
    {
        { "Mongo DeleteOne/DeleteMany", "await collection.DeleteOneAsync(x => x.Id == id, ct);" },
        { "Mongo DeleteOne/DeleteMany", "collection.DeleteMany(Builders<Gift>.Filter.Empty);" },
        { "Mongo FindOneAndDelete", "await gifts.FindOneAndDeleteAsync(filter);" },
        { "Mongo DropCollection/DropDatabase", "await db.DropCollectionAsync(\"gifts\");" },
        { "Mongo TTL index (ExpireAfter)", "new CreateIndexOptions { ExpireAfter = TimeSpan.FromMinutes(10) };" },
        { "S3 DeleteObject", "await s3.DeleteObjectAsync(bucket, key);" },
        { "S3 DeleteObject", "var request = new DeleteObjectRequest { BucketName = b };" },
        { "EF ExecuteDelete", "await db.Payments.Where(p => p.Id == id).ExecuteDeleteAsync(ct);" },
        { "EF Remove/RemoveRange", "using Microsoft.EntityFrameworkCore;\nclass X { void M(Db db, Payment p) { db.Payments.Remove(p); } }" },
        { "EF Remove/RemoveRange", "using Microsoft.EntityFrameworkCore;\nclass X { void M(Db db) { db.RemoveRange(db.Payouts); } }" },
        { "raw SQL DELETE/TRUNCATE", "await db.Database.ExecuteSqlRawAsync(\"delete from qonaqet.payments where id = {0}\", id);" },
        { "raw SQL DELETE/TRUNCATE", "const string Sql = @\"TRUNCATE TABLE qonaqet.ledger_entries\";" },
    };

    [Theory]
    [MemberData(nameof(HardDeleteSamples))]
    public void Each_hard_delete_rule_catches_its_sample(string ruleName, string sample)
    {
        var rule = ForbiddenSource.HardDeleteRules.Single(r => r.Name == ruleName);

        Assert.NotEmpty(rule.Violations("src/Sample.cs", sample));
    }

    [Theory]
    [InlineData("// collection.DeleteOne(x) köhnə kod idi")]
    [InlineData("/* DELETE FROM x */ var a = 1;")]
    [InlineData("list.Remove(item); // EF using-i yoxdur")]
    [InlineData("entity.SoftDelete(now, userId, reason);")]
    [InlineData("var isDeleted = true; var deletedAt = now;")]
    public void Comments_soft_delete_and_plain_collections_are_not_flagged(string sample)
    {
        Assert.All(ForbiddenSource.HardDeleteRules, rule => Assert.Empty(rule.Violations("src/Sample.cs", sample)));
    }

    [Fact]
    public void Source_tree_has_no_hard_deletes()
    {
        var violations = Scan(ForbiddenSource.HardDeleteRules);

        Assert.True(violations.Count == 0, "Hard delete qadağandır (CLAUDE.md → Soft delete):\n" + string.Join('\n', violations));
    }

    [Fact]
    public void Category_code_rule_catches_branching_and_allows_seed_data()
    {
        const string branching = "if (category.Code == \"student\") { limit = 2; }";
        const string switchCase = "var x = code switch { \"veteran-family\" => 3, _ => 1 };";

        Assert.NotEmpty(ForbiddenSource.CategoryCodeLiteral.Violations("src/Qonaqet.Application/Features/Social/Claim.cs", branching));
        Assert.NotEmpty(ForbiddenSource.CategoryCodeLiteral.Violations("src/Qonaqet.Domain/Categories/Limits.cs", switchCase));
        Assert.Empty(ForbiddenSource.CategoryCodeLiteral.Violations("src/Qonaqet.Infrastructure/Persistence/Mongo/Seed/Categories.cs", branching));
    }

    [Fact]
    public void Source_tree_never_branches_on_a_category_code()
    {
        var violations = Scan([ForbiddenSource.CategoryCodeLiteral]);

        Assert.True(violations.Count == 0, "Kateqoriya koduna görə şərt qadağandır (CLAUDE.md → Coding rules):\n" + string.Join('\n', violations));
    }

    [Fact]
    public void IncludeDeleted_rule_allows_only_admin_and_persistence()
    {
        const string call = "var all = await repository.IncludeDeleted().FindAsync(filter, ct);";

        Assert.NotEmpty(ForbiddenSource.IncludeDeletedOutsideAdmin.Violations("src/Qonaqet.Application/Features/Gifts/Queries/X.cs", call));
        Assert.Empty(ForbiddenSource.IncludeDeletedOutsideAdmin.Violations("src/Qonaqet.Application/Features/Admin/Users/X.cs", call));
        Assert.Empty(ForbiddenSource.IncludeDeletedOutsideAdmin.Violations("src/Qonaqet.Infrastructure/Persistence/Mongo/SoftDeleteRepository.cs", call));
    }

    [Fact]
    public void Source_tree_reads_deleted_records_only_in_admin_features()
    {
        var violations = Scan([ForbiddenSource.IncludeDeletedOutsideAdmin]);

        Assert.True(violations.Count == 0, "IncludeDeleted() yalnız admin endpoint-ləri üçündür (CLAUDE.md → Soft delete):\n" + string.Join('\n', violations));
    }

    [Fact]
    public void Comment_stripping_keeps_strings_and_line_numbers()
    {
        const string source = "var a = \"// not a comment\";\n/* line 2\nline 3 */ var b = @\"x\"\"y\";\n// line 4\nvar c = 1;";

        var stripped = SourceText.StripComments(source);

        Assert.Contains("\"// not a comment\"", stripped, StringComparison.Ordinal);
        Assert.Contains("@\"x\"\"y\"", stripped, StringComparison.Ordinal);
        Assert.DoesNotContain("line 2", stripped, StringComparison.Ordinal);
        Assert.Equal(source.Count(c => c == '\n'), stripped.Count(c => c == '\n'));
    }

    private static List<string> Scan(IEnumerable<SourceRule> rules)
    {
        var violations = new List<string>();
        foreach (var file in RepoPaths.SourceFiles())
        {
            var relative = RepoPaths.Relative(file);
            var source = File.ReadAllText(file);
            foreach (var rule in rules)
            {
                violations.AddRange(rule.Violations(relative, source).Select(line => $"{relative}:{line} — {rule.Name}"));
            }
        }

        return violations;
    }
}
