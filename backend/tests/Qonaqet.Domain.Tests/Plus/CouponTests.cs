using Qonaqet.Domain.Plus;
using Qonaqet.Domain.Users;

namespace Qonaqet.Domain.Tests.Plus;

public sealed class CouponTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 8, 0, 0, TimeSpan.Zero);

    private static CouponDetails Details(IReadOnlyList<Guid>? categories = null, int limit = 1, bool active = true) =>
        new("20% endirim", null, null, categories ?? [], limit, Now.AddDays(-1), Now.AddDays(30), active);

    [Fact]
    public void Coupon_without_categories_is_open_to_everyone()
    {
        var coupon = Coupon.Create(BrandId.From(Guid.CreateVersion7()), CouponCodeType.Unique, null, Details(), Now, null);

        Assert.True(coupon.IsOpenTo([]));
        Assert.True(coupon.IsOpenTo([Guid.CreateVersion7()]));
    }

    [Fact]
    public void Coupon_with_categories_is_open_only_to_users_in_one_of_them()
    {
        var student = Guid.CreateVersion7();
        var coupon = Coupon.Create(BrandId.From(Guid.CreateVersion7()), CouponCodeType.Unique, null, Details([student]), Now, null);

        Assert.True(coupon.IsOpenTo([Guid.CreateVersion7(), student]));
        Assert.False(coupon.IsOpenTo([Guid.CreateVersion7()]));
        Assert.False(coupon.IsOpenTo([]));
    }

    [Fact]
    public void Coupon_is_available_only_while_active_and_valid()
    {
        var brand = BrandId.From(Guid.CreateVersion7());
        var coupon = Coupon.Create(brand, CouponCodeType.Unique, null, Details(), Now, null);

        Assert.True(coupon.IsAvailableAt(Now));
        Assert.False(coupon.IsAvailableAt(Now.AddDays(-2)));
        Assert.False(coupon.IsAvailableAt(Now.AddDays(30)));
        Assert.False(Coupon.Create(brand, CouponCodeType.Unique, null, Details(active: false), Now, null).IsAvailableAt(Now));
    }

    [Fact]
    public void Shared_code_belongs_only_to_shared_coupons_and_is_normalized()
    {
        var brand = BrandId.From(Guid.CreateVersion7());

        Assert.Equal("PLUS-20", Coupon.Create(brand, CouponCodeType.Shared, " plus-20 ", Details(), Now, null).SharedCode);
        Assert.Throws<ArgumentException>(() => Coupon.Create(brand, CouponCodeType.Shared, null, Details(), Now, null));
        Assert.Throws<ArgumentException>(() => Coupon.Create(brand, CouponCodeType.Unique, "PLUS-20", Details(), Now, null));
    }

    [Theory]
    [InlineData("abc-123", "ABC-123")]
    [InlineData("AB", null)]
    [InlineData("KOD 1", null)]
    [InlineData("ŞƏKƏR", null)]
    public void Codes_are_upper_case_latin_digits_and_dashes(string input, string? expected) =>
        Assert.Equal(expected, CouponCodes.Normalize(input));

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Per_user_limit_is_between_1_and_100(int limit) =>
        Assert.False(Coupon.IsValid(Details(limit: limit)));

    [Fact]
    public void Assigning_a_brand_grants_and_removing_it_revokes_the_brand_role()
    {
        var user = User.Register("brand@example.az", Now);
        var brand = Guid.CreateVersion7();

        Assert.True(user.AssignBrand(brand, Now, null));
        Assert.True(user.HasRole(Role.BrandUser));
        Assert.False(user.AssignBrand(brand, Now, null));

        Assert.True(user.AssignBrand(null, Now, null));
        Assert.False(user.HasRole(Role.BrandUser));
        Assert.Null(user.BrandId);
    }
}
