using Qonaqet.Domain.Categories;

namespace Qonaqet.Domain.Tests.Categories;

public sealed class CategoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);

    public static VerificationConfig StudentConfig() => VerificationConfig.Create(
        requiresFin: true, minFiles: 1, maxFiles: 3, documentHint: " Tələbə arayışı ",
        subtypes: [],
        fields: [VerificationField.Create("university", "Universitet", VerificationFieldType.Select, required: true, [" BDU ", "ADA"])],
        validity: CategoryValidity.FixedDate(9, 30));

    public static CategoryDetails Details(bool isOpen = false, VerificationConfig? verification = null) => new(
        " Tələbələr ", null, null, "#16b89b", 20, IsActive: true, isOpen, CategoryPrivacyLevel.Public,
        verification ?? StudentConfig(), CategoryLimits.Create(2, 40, poolAccess: true, 500));

    [Theory]
    [InlineData("student", true)]
    [InlineData("veteran-family", true)]
    [InlineData("a1-b2", true)]
    [InlineData("Student", false)]
    [InlineData("-student", false)]
    [InlineData("student-", false)]
    [InlineData("stu--dent", false)]
    [InlineData("tələbə", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Code_is_a_lowercase_slug(string? code, bool valid) => Assert.Equal(valid, CategoryRules.IsValidCode(code));

    [Theory]
    [InlineData("#16B89B", true)]
    [InlineData("#16b89b", true)]
    [InlineData("16B89B", false)]
    [InlineData("#16B89", false)]
    [InlineData("#GGGGGG", false)]
    public void Color_is_hex(string color, bool valid) => Assert.Equal(valid, CategoryRules.IsValidColor(color));

    [Fact]
    public void Create_normalizes_fields_and_keeps_config()
    {
        var category = Category.Create("student", Details(), Now, Guid.CreateVersion7());

        Assert.Equal("student", category.Code);
        Assert.Equal("Tələbələr", category.Name);
        Assert.Equal("#16B89B", category.Color);
        Assert.Equal("Tələbə arayışı", category.Verification.DocumentHint);
        Assert.Equal(["BDU", "ADA"], category.Verification.Fields.Single().Options);
        Assert.Equal(ValidityType.FixedDate, category.Verification.Validity.Type);
        Assert.Equal(40, category.Limits.PerMonth);
        Assert.False(category.IsDeleted);
    }

    [Fact]
    public void Open_category_never_carries_a_verification_config()
    {
        var category = Category.Create("everyone", Details(isOpen: true), Now, null);

        Assert.Same(VerificationConfig.NotRequired, category.Verification);
        Assert.False(category.Verification.RequiresFin);
    }

    [Fact]
    public void ChangeCode_reports_whether_it_changed()
    {
        var category = Category.Create("student", Details(), Now, null);

        Assert.False(category.ChangeCode("student", Now, null));
        Assert.True(category.ChangeCode("students", Now.AddMinutes(1), null));
        Assert.Equal("students", category.Code);
        Assert.Throws<ArgumentException>(() => category.ChangeCode("Bad Code", Now, null));
    }

    [Fact]
    public void Verification_config_rejects_bad_shapes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VerificationConfig.Create(false, 3, 2, null, [], [], CategoryValidity.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => VerificationConfig.Create(false, 0, 11, null, [], [], CategoryValidity.None));
        Assert.Throws<ArgumentException>(() => VerificationConfig.Create(
            false, 0, 1, null, [CategorySubtype.Create("qazi", "Qazi"), CategorySubtype.Create("qazi", "Yenə")], [], CategoryValidity.None));
        Assert.Throws<ArgumentException>(() => VerificationField.Create("university", "Universitet", VerificationFieldType.Select, true, []));
        Assert.Throws<ArgumentException>(() => VerificationField.Create("University", "Universitet", VerificationFieldType.Text, true, null));
    }

    [Theory]
    [InlineData(ValidityType.None, null, null, null, true)]
    [InlineData(ValidityType.FixedDate, 9, 30, null, true)]
    [InlineData(ValidityType.FixedDate, 2, 29, null, false)]
    [InlineData(ValidityType.FixedDate, 9, 31, null, false)]
    [InlineData(ValidityType.FixedDate, 13, 1, null, false)]
    [InlineData(ValidityType.Months, null, null, 12, true)]
    [InlineData(ValidityType.Months, null, null, 0, false)]
    [InlineData(ValidityType.Months, 1, null, 6, false)]
    public void Validity_rules(ValidityType type, int? month, int? day, int? months, bool valid) =>
        Assert.Equal(valid, CategoryValidity.IsValid(type, month, day, months));

    [Theory]
    [InlineData(1, 15, false, 0, true)]
    [InlineData(2, 40, true, 500, true)]
    [InlineData(5, 4, false, 0, false)]
    [InlineData(1, 15, false, 500, false)]
    [InlineData(-1, 15, false, 0, false)]
    public void Limit_rules(int perDay, int perMonth, bool poolAccess, long poolSpend, bool valid) =>
        Assert.Equal(valid, CategoryLimits.IsValid(perDay, perMonth, poolAccess, poolSpend));

    [Theory]
    [InlineData(VerificationFieldType.Text, "Bakı", true)]
    [InlineData(VerificationFieldType.Date, "2026-09-30", true)]
    [InlineData(VerificationFieldType.Date, "30.09.2026", false)]
    public void Field_accepts_values_by_type(VerificationFieldType type, string value, bool accepted) =>
        Assert.Equal(accepted, VerificationField.Create("value", "Dəyər", type, true, null).Accepts(value));

    [Fact]
    public void Select_field_accepts_only_its_options()
    {
        var field = StudentConfig().Fields.Single();

        Assert.True(field.Accepts("ADA"));
        Assert.False(field.Accepts("ada"));
        Assert.False(field.Accepts("Harvard"));
    }
}

public sealed class PoolTests
{
    [Fact]
    public void Default_pool_is_empty_and_has_no_campaign()
    {
        var categoryId = Qonaqet.SharedKernel.StronglyTypedId.New<CategoryId>();
        var pool = Qonaqet.Domain.Social.Pool.CreateDefault(categoryId, DateTimeOffset.UtcNow, null);

        Assert.True(pool.IsDefault);
        Assert.Equal(categoryId, pool.CategoryId);
        Assert.Equal(0, pool.BalanceQepik);
        Assert.Equal(0, pool.ReservedQepik);
    }
}
