using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Tests.Categories;
using Qonaqet.Domain.Verification;

namespace Qonaqet.Domain.Tests.Verification;

public sealed class VerificationRulesTests
{
    private static readonly VerificationConfig Veteran = VerificationConfig.Create(
        true, 1, 3, null, [CategorySubtype.Create("qazi", "Qazi"), CategorySubtype.Create("martyr-family", "Şəhid ailəsi")],
        [VerificationField.Create("issuedOn", "Arayışın tarixi", VerificationFieldType.Date, required: false, null)], CategoryValidity.ForMonths(12));

    [Theory]
    [InlineData("5abc12d", "5ABC12D")]
    [InlineData(" 5ABC12D ", "5ABC12D")]
    [InlineData("5ABC12", null)]
    [InlineData("5ABC12DE", null)]
    [InlineData("5ABÇ12D", null)]
    [InlineData(null, null)]
    public void Fin_is_seven_latin_letters_or_digits(string? fin, string? expected) => Assert.Equal(expected, VerificationRules.NormalizeFin(fin));

    [Fact]
    public void Valid_answer_has_no_problems()
    {
        var problems = VerificationRules.Check(
            CategoryTests.StudentConfig(), null, new Dictionary<string, string> { ["university"] = "ADA" }, "5ABC12D", 1);

        Assert.Empty(problems);
    }

    [Fact]
    public void Every_rule_comes_from_the_config()
    {
        var problems = VerificationRules.Check(
            Veteran, "unknown", new Dictionary<string, string> { ["issuedOn"] = "dünən", ["extra"] = "x" }, null, 4);

        Assert.Equal(
            ["subtype_invalid", "field_unknown", "field_invalid", "fin_invalid", "files_count_invalid"],
            problems.Select(p => p.Code).ToArray());
        Assert.Contains(problems, p => p.Field == "fields.issuedOn");
    }

    [Fact]
    public void Missing_required_field_and_unexpected_values()
    {
        var noFin = VerificationConfig.Create(false, 0, 1, null, [], [], CategoryValidity.None);

        Assert.Contains(VerificationRules.Check(CategoryTests.StudentConfig(), null, new Dictionary<string, string>(), "5ABC12D", 1), p => p.Code == "field_required");
        Assert.Equal(["subtype_not_expected", "fin_not_expected"],
            VerificationRules.Check(noFin, "qazi", new Dictionary<string, string>(), "5ABC12D", 0).Select(p => p.Code).ToArray());
    }

    [Fact]
    public void Sensitive_category_name_is_never_shown_to_other_parties()
    {
        var details = CategoryTests.Details();
        var publicCategory = Category.Create("student", details, DateTimeOffset.UtcNow, null);
        var sensitive = Category.Create("hidden", details with { PrivacyLevel = CategoryPrivacyLevel.Sensitive, Name = "Həssas" }, DateTimeOffset.UtcNow, null);

        Assert.Equal("Tələbələr", CategoryLabel.ForOtherParties(publicCategory));
        Assert.Equal("Qonaq", CategoryLabel.ForOtherParties(sensitive));
        Assert.Equal("Qonaq", CategoryLabel.ForOtherParties(null));
    }
}
