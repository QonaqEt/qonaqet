using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Catalog;
using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Social;
using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Tests.Gifts;

public sealed class GiftTests
{
    internal static readonly DateTimeOffset Now = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);
    internal static readonly BusinessId Shop = StronglyTypedId.New<BusinessId>();
    internal static readonly CatalogItemId Tea = StronglyTypedId.New<CatalogItemId>();
    internal static readonly CatalogItemId Cake = StronglyTypedId.New<CatalogItemId>();
    private static readonly GiftLinkSecret Link = new("hash-1", "sealed-1");

    internal static Gift Create(
        GiftContent? content = null, string? email = "ana@example.az", SenderDisplay display = SenderDisplay.Named, DateTimeOffset? deliverAt = null,
        GiftExpiryPlan? expiry = null) =>
        Gift.Create(
            content ?? GiftContent.ForItems(Shop, [GiftItem.Create(Tea, "Çay", 2_00, 2), GiftItem.Create(Cake, "Paxlava", 5_00, 1)]),
            new GiftSender(Guid.CreateVersion7(), null, display, "Leyla"),
            new GiftRecipient("Ana", email),
            GiftTheme.Family,
            "Səni sevirəm",
            deliverAt ?? Now,
            GiftRules.DefaultValidityDays,
            expiry ?? new GiftExpiryPlan(GiftExpiryAction.RefundToPayer, null),
            Now);

    internal static Gift Delivered(GiftContent? content = null, GiftExpiryPlan? expiry = null)
    {
        var gift = Create(content, expiry: expiry);
        gift.AttachPayment(Guid.CreateVersion7());
        Assert.True(gift.ConfirmPaid(Link, Now));
        gift.Deliver(null, Now);
        gift.DequeueDomainEvents();
        return gift;
    }

    [Fact]
    public void Items_gift_is_priced_from_the_snapshot_and_awaits_payment()
    {
        var gift = Create();

        Assert.Equal(GiftKind.Items, gift.Kind);
        Assert.Equal(GiftScope.Business, gift.Scope);
        Assert.Equal(Shop, gift.BusinessId);
        Assert.Equal(2 * 2_00 + 5_00, gift.AmountQepik);
        Assert.Equal(gift.AmountQepik, gift.RemainingQepik);
        Assert.Equal(FulfillmentType.Pickup, gift.FulfillmentType);
        Assert.Equal(GiftStatus.AwaitingPayment, gift.Status);
        Assert.Equal(Now.AddDays(60), gift.ExpiresAt);
        Assert.Equal(3, gift.RemainingUnits);
        Assert.Null(gift.LinkTokenHash);
    }

    [Fact]
    public void Items_gift_allows_one_to_ten_units()
    {
        Assert.Throws<ArgumentException>(() => GiftContent.ForItems(Shop, []));
        Assert.Throws<ArgumentException>(() => GiftContent.ForItems(Shop, [GiftItem.Create(Tea, "Çay", 1_00, 6), GiftItem.Create(Cake, "Paxlava", 1_00, 5)]));
        Assert.Throws<ArgumentException>(() => GiftContent.ForItems(Shop, [GiftItem.Create(Tea, "Çay", 1_00, 1), GiftItem.Create(Tea, "Çay", 1_00, 1)]));
        Assert.Equal(10, GiftContent.ForItems(Shop, [GiftItem.Create(Tea, "Çay", 1_00, 10)]).Items[0].Qty);
    }

    [Fact]
    public void Network_voucher_is_two_to_two_hundred_azn_and_has_no_business()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GiftContent.ForNetworkVoucher(1_99));
        Assert.Throws<ArgumentOutOfRangeException>(() => GiftContent.ForNetworkVoucher(200_01));
        var content = GiftContent.ForNetworkVoucher(200_00);
        Assert.Equal(GiftKind.Voucher, content.Kind);
        Assert.Equal(GiftScope.Network, content.Scope);
        Assert.Null(content.BusinessId);
    }

    [Fact]
    public void Delivery_is_at_most_ninety_days_ahead_and_past_means_now()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(deliverAt: Now.AddDays(91)));
        Assert.Equal(Now, Create(deliverAt: Now.AddDays(-1)).DeliverAt);
        var scheduled = Create(deliverAt: Now.AddDays(10));
        Assert.Equal(Now.AddDays(70), scheduled.ExpiresAt);
    }

    [Fact]
    public void Paid_gift_is_scheduled_once_and_keeps_the_sealed_token_until_email_and_reveal()
    {
        var gift = Create();
        gift.AttachPayment(Guid.CreateVersion7());

        Assert.True(gift.ConfirmPaid(Link, Now));
        Assert.False(gift.ConfirmPaid(new GiftLinkSecret("other", "other"), Now));
        Assert.Equal(GiftStatus.Scheduled, gift.Status);
        Assert.Equal("hash-1", gift.LinkTokenHash);

        Assert.True(gift.RevealLink(Now));
        Assert.False(gift.RevealLink(Now));
        Assert.Equal("sealed-1", gift.LinkTokenSealed); // email hələ getməyib

        gift.Deliver(null, Now);
        Assert.Null(gift.LinkTokenSealed);
        Assert.IsType<GiftDelivered>(Assert.Single(gift.DequeueDomainEvents()));
    }

    [Fact]
    public void Link_only_gift_drops_the_sealed_token_after_the_sender_takes_it()
    {
        var gift = Create(email: null);
        gift.AttachPayment(Guid.CreateVersion7());
        gift.ConfirmPaid(Link, Now);
        gift.Deliver(null, Now);
        Assert.Equal("sealed-1", gift.LinkTokenSealed);

        Assert.True(gift.RevealLink(Now));
        Assert.Null(gift.LinkTokenSealed);
    }

    [Fact]
    public void Reissue_replaces_the_hash()
    {
        var gift = Delivered();
        gift.ReissueLink(new GiftLinkSecret("hash-2", "sealed-2"), Now, gift.SenderUserId);

        Assert.Equal("hash-2", gift.LinkTokenHash);
        Assert.Null(gift.LinkTokenSealed);
        Assert.NotNull(gift.LinkRevealedAt);
    }

    [Fact]
    public void First_open_sets_opened_and_claim_binds_one_account()
    {
        var gift = Delivered();
        Assert.True(gift.Open(Now));
        Assert.False(gift.Open(Now));
        Assert.Equal(GiftStatus.Opened, gift.Status);

        var user = Guid.CreateVersion7();
        Assert.True(gift.ClaimTo(user, Now));
        Assert.True(gift.ClaimTo(user, Now));
        Assert.False(gift.ClaimTo(Guid.CreateVersion7(), Now));
        Assert.Equal(user, gift.RecipientUserId);
    }

    [Fact]
    public void Voucher_is_redeemed_partially_until_nothing_remains()
    {
        var gift = Delivered(GiftContent.ForBusinessVoucher(Shop, 50_00));
        var staff = Guid.CreateVersion7();
        var version = gift.Version;

        var first = gift.Redeem(Shop, false, null, 20_00, staff, Now);
        Assert.Equal(20_00, first.ValueQepik);
        Assert.Equal(30_00, gift.RemainingQepik);
        Assert.Equal(GiftStatus.PartiallyRedeemed, gift.Status);
        Assert.True(gift.Version > version);

        Assert.Equal(GiftRedeemRejection.InsufficientBalance, gift.CheckRedeem(Shop, false, null, 30_01, Now));
        gift.Redeem(Shop, false, null, 30_00, staff, Now);
        Assert.Equal(GiftStatus.Redeemed, gift.Status);
        Assert.Equal(GiftRedeemRejection.AlreadyRedeemed, gift.CheckRedeem(Shop, false, null, 1, Now));

        var events = gift.DequeueDomainEvents().OfType<GiftRedeemed>().ToList();
        Assert.Equal([20_00L, 30_00L], events.Select(e => e.ValueQepik));
    }

    [Fact]
    public void Items_are_redeemed_per_line()
    {
        var gift = Delivered();
        Assert.Equal(GiftRedeemRejection.InsufficientBalance, gift.CheckRedeem(Shop, false, [new GiftLine(Tea, 3)], null, Now));
        Assert.Equal(GiftRedeemRejection.InvalidSelection, gift.CheckRedeem(Shop, false, null, 1_00, Now));
        Assert.Equal(GiftRedeemRejection.InvalidSelection, gift.CheckRedeem(Shop, false, [new GiftLine(StronglyTypedId.New<CatalogItemId>(), 1)], null, Now));

        var redemption = gift.Redeem(Shop, false, [new GiftLine(Tea, 1)], null, Guid.CreateVersion7(), Now);
        Assert.Equal(2_00, redemption.ValueQepik);
        Assert.Null(redemption.AmountQepik);
        Assert.Equal(1, gift.Items[0].RemainingQty);
        Assert.Equal(7_00, gift.RemainingQepik);
        Assert.Equal(1, Assert.Single(gift.DequeueDomainEvents().OfType<GiftRedeemed>()).Units);
    }

    [Fact]
    public void Business_scope_and_network_acceptance_are_enforced()
    {
        var other = StronglyTypedId.New<BusinessId>();
        Assert.Equal(GiftRedeemRejection.WrongBusiness, Delivered().CheckRedeem(other, true, [new GiftLine(Tea, 1)], null, Now));

        var network = Delivered(GiftContent.ForNetworkVoucher(10_00));
        Assert.Equal(GiftRedeemRejection.WrongBusiness, network.CheckRedeem(other, false, null, 1_00, Now));
        Assert.Null(network.CheckRedeem(other, true, null, 1_00, Now));
    }

    [Fact]
    public void Undelivered_and_expired_gifts_cannot_be_redeemed()
    {
        Assert.Equal(GiftRedeemRejection.NotRedeemable, Create().CheckRedeem(Shop, false, [new GiftLine(Tea, 1)], null, Now));
        var gift = Delivered();
        Assert.Equal(GiftRedeemRejection.Expired, gift.CheckRedeem(Shop, false, [new GiftLine(Tea, 1)], null, gift.ExpiresAt));
    }

    [Fact]
    public void Only_a_scheduled_gift_can_be_cancelled_with_a_full_refund()
    {
        var gift = Create();
        var payment = Guid.CreateVersion7();
        gift.AttachPayment(payment);
        gift.ConfirmPaid(Link, Now);

        gift.Cancel(Now, gift.SenderUserId);
        Assert.Equal(GiftStatus.Cancelled, gift.Status);
        Assert.Null(gift.LinkTokenSealed);
        var refund = Assert.IsType<GiftRefundRequested>(Assert.Single(gift.DequeueDomainEvents()));
        Assert.Equal((payment, 9_00L, GiftRefundReason.Cancelled), (refund.PaymentId, refund.AmountQepik, refund.Reason));
        Assert.Throws<InvalidOperationException>(() => gift.Cancel(Now, null));
        Assert.Throws<InvalidOperationException>(() => Delivered().Cancel(Now, null));

        Assert.True(gift.MarkRefunded(9_00, Now));
        Assert.False(gift.MarkRefunded(9_00, Now));
        Assert.Equal(GiftStatus.Cancelled, gift.Status);
    }

    [Fact]
    public void Expiry_refunds_the_remaining_value_or_donates_it()
    {
        var refunded = Delivered(GiftContent.ForBusinessVoucher(Shop, 50_00));
        refunded.Redeem(Shop, false, null, 20_00, Guid.CreateVersion7(), Now);
        refunded.DequeueDomainEvents();
        Assert.Throws<InvalidOperationException>(() => refunded.Expire(Now));

        refunded.Expire(refunded.ExpiresAt);
        Assert.Equal(GiftStatus.Expired, refunded.Status);
        var expiredEvents = refunded.DequeueDomainEvents();
        Assert.Equal(30_00, Assert.IsType<GiftExpired>(expiredEvents[0]).RemainingQepik);
        Assert.Equal(30_00, Assert.IsType<GiftRefundRequested>(Assert.Single(expiredEvents.Skip(1))).AmountQepik);
        Assert.True(refunded.MarkRefunded(30_00, Now));
        Assert.Equal(GiftStatus.Refunded, refunded.Status);

        var pool = StronglyTypedId.New<PoolId>();
        var donated = Delivered(GiftContent.ForNetworkVoucher(10_00), new GiftExpiryPlan(GiftExpiryAction.DonateToPool, pool));
        donated.Expire(donated.ExpiresAt);
        var donation = Assert.IsType<GiftExpiryDonated>(Assert.Single(donated.DequeueDomainEvents().Skip(1)));
        Assert.Equal((pool, 10_00L), (donation.PoolId, donation.AmountQepik));
        Assert.Equal(10_00, donated.DonatedQepik);
    }

    [Fact]
    public void Anonymous_sender_is_shown_as_anonymous_friend()
    {
        Assert.Equal("Anonim dost", Create(display: SenderDisplay.Anonymous).SenderDisplayName);
        Assert.Equal("Leyla", Create().SenderDisplayName);
    }
}
