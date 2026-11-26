using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Social;
using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Tests.Social;

public sealed class ClaimTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);

    private static Offer Daily() => Offer.CreateDaily(
        StronglyTypedId.New<BusinessId>(), StronglyTypedId.New<CategoryId>(),
        new DailyOfferDetails("Çay", null, 5, null, null, DonorDisplay.Anonymous, null, true), Now, null);

    private static Claim Reserve(Offer? offer = null) =>
        Claim.ReserveFromOffer(offer ?? Daily(), Guid.CreateVersion7(), "Bir xeyirxah insan sizə ısmarladı 💚", "2026-11-20", Now);

    [Fact]
    public void Reservation_lasts_thirty_minutes_and_snapshots_the_offer()
    {
        var offer = Daily();
        var claim = Reserve(offer);

        Assert.Equal(ClaimStatus.Reserved, claim.Status);
        Assert.Equal(Now.AddMinutes(30), claim.ExpiresAt);
        Assert.Equal(offer.Id, claim.OfferId);
        Assert.Equal(OfferType.BusinessDaily, claim.OfferType);
        Assert.Equal("Çay", claim.Title);
        Assert.Equal("2026-11-20", claim.QuotaDate);
        Assert.True(claim.IsRedeemableAt(Now.AddMinutes(29)));
        Assert.False(claim.IsRedeemableAt(Now.AddMinutes(30)));
        Assert.False(claim.IsCommissionable);
    }

    [Fact]
    public void Redeem_raises_one_event_and_cannot_repeat()
    {
        var claim = Reserve();
        var staff = Guid.CreateVersion7();

        claim.Redeem(staff, Now.AddMinutes(5));

        Assert.Equal(ClaimStatus.Redeemed, claim.Status);
        Assert.Equal(staff, claim.RedeemedByStaffId);
        var evt = Assert.IsType<ClaimRedeemed>(Assert.Single(claim.DequeueDomainEvents()));
        Assert.False(evt.Commissionable);
        Assert.Null(evt.AmountQepik);
        Assert.Throws<InvalidOperationException>(() => claim.Redeem(staff, Now.AddMinutes(6)));
    }

    [Fact]
    public void Redeemed_event_carries_who_funded_the_treat_for_scoring()
    {
        var donor = Guid.CreateVersion7();
        var remote = Offer.CreateRemote(
            StronglyTypedId.New<BusinessId>(), StronglyTypedId.New<CategoryId>(), StronglyTypedId.New<Catalog.CatalogItemId>(), "Kofe", 4_00, 2,
            DonorDisplay.Anonymous, null, donor, Now);
        var fromRemote = Reserve(remote);
        var fromDaily = Reserve();
        var corporate = Guid.CreateVersion7();
        var pool = Pool.CreateForCampaign(StronglyTypedId.New<CategoryId>(), Guid.CreateVersion7(), Now, null);
        var fromPool = Claim.ReserveFromPool(
            pool, StronglyTypedId.New<BusinessId>(), StronglyTypedId.New<Catalog.CatalogItemId>(), "Çay", 2_00, Guid.CreateVersion7(),
            "Bir xeyirxah insan sizə ısmarladı 💚", Guid.CreateVersion7(), donor, corporate, Now);

        Assert.Equal((donor, (Guid?)null), Funders(fromRemote));
        Assert.Equal(((Guid?)null, (Guid?)null), Funders(fromDaily));
        Assert.Equal((donor, corporate), Funders(fromPool));
    }

    [Fact]
    public void Donor_who_claims_their_own_remote_treat_is_not_named_as_the_funder()
    {
        var donor = Guid.CreateVersion7();
        var remote = Offer.CreateRemote(
            StronglyTypedId.New<BusinessId>(), StronglyTypedId.New<CategoryId>(), StronglyTypedId.New<Catalog.CatalogItemId>(), "Kofe", 4_00, 2,
            DonorDisplay.Anonymous, null, donor, Now);
        var own = Claim.ReserveFromOffer(remote, donor, "Bir xeyirxah insan sizə ısmarladı 💚", "2026-11-20", Now);

        Assert.Equal(((Guid?)null, (Guid?)null), Funders(own));
    }

    private static (Guid? User, Guid? Corporate) Funders(Claim claim)
    {
        claim.DequeueDomainEvents();
        claim.Redeem(Guid.CreateVersion7(), Now.AddMinutes(1));
        var evt = Assert.IsType<ClaimRedeemed>(Assert.Single(claim.DequeueDomainEvents()));
        return (evt.FunderUserId, evt.FunderCorporateId);
    }

    [Fact]
    public void Expired_reservation_cannot_be_redeemed_and_closed_claims_cannot_change()
    {
        var late = Reserve();
        Assert.Throws<InvalidOperationException>(() => late.Redeem(Guid.CreateVersion7(), Now.AddMinutes(31)));

        late.Expire(Now.AddMinutes(31));
        Assert.Equal(ClaimStatus.Expired, late.Status);
        Assert.Throws<InvalidOperationException>(() => late.Cancel("user", Now, null));

        var cancelled = Reserve();
        cancelled.Cancel("user_cancelled", Now, null);
        Assert.Equal("user_cancelled", cancelled.CancelReason);
        Assert.Throws<InvalidOperationException>(() => cancelled.Expire(Now));
    }

    [Fact]
    public void Short_code_hash_is_attached_once()
    {
        var claim = Reserve();
        claim.AttachShortCode("abc");
        Assert.Throws<InvalidOperationException>(() => claim.AttachShortCode("def"));
    }
}
