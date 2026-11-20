using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Social;
using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Tests.Social;

public sealed class OfferTests
{
    // 2026-11-20 08:00 UTC = 12:00 Bakı vaxtı.
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);

    private static readonly BusinessId Business = StronglyTypedId.New<BusinessId>();
    private static readonly CategoryId Category = StronglyTypedId.New<CategoryId>();

    private static DailyOfferDetails Daily(int quota = 20, string? from = null, string? to = null, DonorDisplay display = DonorDisplay.Named, string? name = null) =>
        new("Çay", null, quota, from, to, display, name, true);

    [Fact]
    public void Daily_offer_starts_with_full_quota_and_resets_lazily_by_baku_date()
    {
        var offer = Offer.CreateDaily(Business, Category, Daily(), Now, null);

        Assert.Equal(OfferType.BusinessDaily, offer.Type);
        Assert.Equal(20, offer.DailyQuota);
        Assert.Equal(0, offer.UsedToday);
        Assert.Null(offer.QuotaDate);
        Assert.Equal(20, offer.AvailableToday(BakuTime.ToBakuDate(Now)));
        Assert.Equal("2026-11-20", Offer.DateKey(BakuTime.ToBakuDate(Now)));
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("09:00", "18:00", true)]
    [InlineData("13:00", "18:00", false)]
    [InlineData("22:00", "13:00", true)]   // gecəyarısını keçən pəncərə
    [InlineData("22:00", "11:00", false)]
    public void Window_is_checked_in_baku_time(string? from, string? to, bool open)
    {
        var offer = Offer.CreateDaily(Business, Category, Daily(from: from, to: to), Now, null);
        Assert.Equal(open, offer.IsWithinWindow(Now));
    }

    [Theory]
    [InlineData("09:00", null)]
    [InlineData("9:00", "18:00")]
    [InlineData("09:00", "09:00")]
    [InlineData("25:00", "18:00")]
    public void Invalid_windows_are_rejected(string? from, string? to)
    {
        Assert.False(Offer.IsValidWindow(from, to));
        Assert.ThrowsAny<ArgumentException>(() => Offer.CreateDaily(Business, Category, Daily(from: from, to: to), Now, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public void Quota_must_be_in_range(int quota) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Offer.CreateDaily(Business, Category, Daily(quota), Now, null));

    [Fact]
    public void Suspended_offer_counts_down_from_the_quantity_and_remembers_the_staff()
    {
        var staff = Guid.CreateVersion7();
        var donor = Guid.CreateVersion7();
        var offer = Offer.CreateSuspended(Business, Category, new SuspendedOfferDetails("Kofe", null, 5, DonorDisplay.Named, " Leyla ", donor), staff, Now);

        Assert.Equal(OfferType.Suspended, offer.Type);
        Assert.Equal(5, offer.TotalQuantity);
        Assert.Equal(5, offer.Remaining);
        Assert.Equal(staff, offer.CreatedByStaffId);
        Assert.Equal(donor, offer.DonorUserId);
        Assert.Equal("Leyla", offer.DonorName);
        Assert.True(offer.IsActive);
        Assert.Throws<InvalidOperationException>(() => offer.UpdateDaily(Daily(), Now, staff));
    }

    [Fact]
    public void Anonymous_display_never_keeps_a_name()
    {
        var offer = Offer.CreateSuspended(
            Business, Category, new SuspendedOfferDetails("Kofe", null, 1, DonorDisplay.Anonymous, "Leyla", null), Guid.CreateVersion7(), Now);
        Assert.Null(offer.DonorName);

        offer.UpdateSuspended("Kofe", DonorDisplay.Named, "Leyla", false, Now, null);
        Assert.Equal("Leyla", offer.DonorName);
        Assert.False(offer.IsActive);
    }
}
