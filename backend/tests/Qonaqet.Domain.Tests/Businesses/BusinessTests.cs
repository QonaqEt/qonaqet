using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Users;

namespace Qonaqet.Domain.Tests.Businesses;

public sealed class BusinessTests
{
    public const string ValidIban = "AZ21NABZ00000000137010001944";

    private static readonly DateTimeOffset Now = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);

    public static BusinessProfile Profile(string voen = "1234567890") => new(
        "Çay evi", BusinessType.Cafe, voen, "Bakı, Nizami küç. 10", GeoPoint.At(40.4093, 49.8671), "+994 50 123 45 67",
        "Info@Cay.az", "az21 nabz 0000 0000 1370 1000 1944", [DailyHours.Create(DayOfWeek.Monday, "09:00", "22:00")], null, null);

    [Theory]
    [InlineData("1234567890", true)]
    [InlineData("123456789", false)]
    [InlineData("12345678a0", false)]
    [InlineData(null, false)]
    public void Voen_is_ten_digits(string? voen, bool valid) => Assert.Equal(valid, BusinessRules.IsValidVoen(voen));

    [Theory]
    [InlineData("AZ21NABZ00000000137010001944", "AZ21NABZ00000000137010001944")]
    [InlineData("az21 nabz 0000 0000 1370 1000 1944", "AZ21NABZ00000000137010001944")]
    [InlineData("AZ22NABZ00000000137010001944", null)]
    [InlineData("DE89370400440532013000", null)]
    [InlineData("AZ21NABZ0000000013701000194", null)]
    public void Iban_is_normalized_and_checked_with_mod97(string iban, string? expected) => Assert.Equal(expected, BusinessRules.NormalizeIban(iban));

    [Theory]
    [InlineData("+994 50 123 45 67", "+994501234567")]
    [InlineData("050-123-45-67", "+994501234567")]
    [InlineData("994501234567", "+994501234567")]
    [InlineData("+1 555 123 4567", null)]
    [InlineData("12345", null)]
    public void Phone_is_normalized_to_e164(string phone, string? expected) => Assert.Equal(expected, BusinessRules.NormalizePhone(phone));

    [Theory]
    [InlineData("Çay Evi Şəki", "cay-evi-seki")]
    [InlineData("  Gülüstan & Ömər  ", "gulustan-omer")]
    [InlineData("İlk Kafe", "ilk-kafe")]
    [InlineData("!!!", "biznes")]
    public void Slug_transliterates_azerbaijani_letters(string name, string slug) => Assert.Equal(slug, BusinessRules.Slugify(name));

    [Fact]
    public void Submitted_business_is_pending_with_normalized_contacts()
    {
        var applicant = Guid.CreateVersion7();
        var business = Business.Submit(Profile(), "cay-evi", applicant, Now);

        Assert.Equal(BusinessStatus.Pending, business.Status);
        Assert.Equal("+994501234567", business.Phone);
        Assert.Equal("info@cay.az", business.Email);
        Assert.Equal(ValidIban, business.Iban);
        Assert.Equal([49.8671, 40.4093], business.Location.Coordinates);
        Assert.Equal(applicant, business.OwnerUserId);
        Assert.Single(business.DomainEvents.OfType<BusinessApplied>());
    }

    [Fact]
    public void Activation_and_suspension_follow_the_status_flow()
    {
        var business = Business.Submit(Profile(), "cay-evi", Guid.CreateVersion7(), Now);

        Assert.True(business.Activate(Now, null));
        Assert.False(business.Activate(Now, null));
        Assert.True(business.Suspend("Şikayət", Now.AddDays(1), null));
        Assert.Equal(BusinessStatus.Suspended, business.Status);
        Assert.True(business.Activate(Now.AddDays(2), null));
        Assert.Equal(Now, business.ActivatedAt);
        Assert.Null(business.SuspendReason);
    }

    [Fact]
    public void Settings_reject_min_above_max()
    {
        var business = Business.Submit(Profile(), "cay-evi", Guid.CreateVersion7(), Now);

        Assert.Throws<ArgumentOutOfRangeException>(() => business.ChangeSettings(true, true, 5_000, 1_000, Now, null));
        business.ChangeSettings(true, true, 1_000, 5_000, Now, null);
        Assert.Equal((1_000, 5_000), (business.VoucherMinQepik, business.VoucherMaxQepik));
    }

    [Fact]
    public void Business_roles_follow_memberships()
    {
        var user = User.Register("o@example.az", Now);
        var businessId = Guid.CreateVersion7();

        Assert.True(user.SyncBusinessRoles(true, false, businessId, Now, null));
        Assert.False(user.SyncBusinessRoles(true, false, businessId, Now, null));
        Assert.Equal([Role.User, Role.BusinessOwner], user.Roles);
        Assert.Equal(businessId, user.BusinessId);

        user.SyncBusinessRoles(false, false, null, Now, null);
        Assert.Equal([Role.User], user.Roles);
        Assert.Null(user.BusinessId);
    }
}
