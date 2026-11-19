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

public sealed class ValidityTests
{
    // 2026-11-19 10:00 Bakı (06:00 UTC).
    private static readonly DateTimeOffset Approved = new(2026, 11, 19, 6, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset EndOfBakuDay(int year, int month, int day) => new(year, month, day, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void None_never_expires() => Assert.Null(CategoryValidity.None.ValidUntil(Approved));

    [Fact]
    public void Months_count_from_the_baku_approval_day_to_its_end()
    {
        // 19 may 2027 Bakı gününün sonu = 2027-05-19T20:00Z (UTC+4).
        Assert.Equal(EndOfBakuDay(2027, 5, 19), CategoryValidity.ForMonths(6).ValidUntil(Approved));
        Assert.Equal(EndOfBakuDay(2027, 11, 19), CategoryValidity.ForMonths(12).ValidUntil(Approved));
    }

    [Fact]
    public void Fixed_date_is_the_next_occurrence_including_today()
    {
        Assert.Equal(EndOfBakuDay(2027, 9, 30), CategoryValidity.FixedDate(9, 30).ValidUntil(Approved));
        Assert.Equal(EndOfBakuDay(2026, 11, 19), CategoryValidity.FixedDate(11, 19).ValidUntil(Approved));
        Assert.Equal(EndOfBakuDay(2026, 12, 31), CategoryValidity.FixedDate(12, 31).ValidUntil(Approved));
    }

    [Fact]
    public void Baku_day_boundary_is_used_not_utc()
    {
        // 2026-09-30T21:30Z artıq Bakıda 1 oktyabrdır → növbəti il.
        var lateUtc = new DateTimeOffset(2026, 9, 30, 21, 30, 0, TimeSpan.Zero);
        Assert.Equal(EndOfBakuDay(2027, 9, 30), CategoryValidity.FixedDate(9, 30).ValidUntil(lateUtc));
    }

    [Fact]
    public void Verified_category_validity_is_logical()
    {
        var user = Qonaqet.Domain.Users.User.Register("u@example.az", Approved);
        var categoryId = Guid.CreateVersion7();
        var verificationId = Guid.CreateVersion7();
        user.VerifyCategory(categoryId, null, Approved.AddDays(1), verificationId, Approved, null);

        var entry = user.VerifiedCategoryFor(categoryId)!;
        Assert.True(entry.IsValidAt(Approved));
        Assert.False(entry.IsValidAt(Approved.AddDays(1)));

        // Yeniləmə köhnə qeydi əvəz edir.
        user.VerifyCategory(categoryId, "qazi", null, Guid.CreateVersion7(), Approved, null);
        Assert.Single(user.VerifiedCategories);
        Assert.True(user.VerifiedCategories[0].IsValidAt(Approved.AddYears(50)));
        Assert.False(user.OverrideCategoryValidity(verificationId, null, Approved, null));
    }
}
