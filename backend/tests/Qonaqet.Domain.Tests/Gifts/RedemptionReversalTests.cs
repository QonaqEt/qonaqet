using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Gifts;
using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Tests.Gifts;

/// <summary>Təhvilin geri alınması (E13-T05): qalıq bərpası, mənfi tərs yazı, hadisə.</summary>
public sealed class RedemptionReversalTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Reversing_a_voucher_redemption_restores_the_balance_and_writes_a_negative_entry()
    {
        var gift = GiftTests.Delivered(GiftContent.ForBusinessVoucher(Shop(), 50_00));
        var actor = Guid.CreateVersion7();
        var first = gift.Redeem(gift.BusinessId!.Value, false, null, 20_00, actor, Now);
        var second = gift.Redeem(gift.BusinessId!.Value, false, null, 10_00, actor, Now);
        gift.DequeueDomainEvents();

        var reversal = gift.ReverseRedemption(second, actor, Now.AddMinutes(1));

        Assert.Equal((30_00L, 20_00L, GiftStatus.PartiallyRedeemed), (gift.RemainingQepik, gift.RedeemedQepik, gift.Status));
        Assert.Equal((-10_00L, -10_00L, second.Id), (reversal.ValueQepik, reversal.AmountQepik!.Value, reversal.ReversesRedemptionId!.Value));
        Assert.True(reversal.IsReversal);
        var evt = Assert.IsType<GiftRedemptionReversed>(Assert.Single(gift.DequeueDomainEvents()));
        Assert.Equal((second.Id, 10_00L, false), (evt.RedemptionId, evt.ValueQepik, evt.FullyUnredeemed));

        gift.ReverseRedemption(first, actor, Now.AddMinutes(2));
        Assert.Equal((50_00L, 0L, GiftStatus.Delivered), (gift.RemainingQepik, gift.RedeemedQepik, gift.Status));
        Assert.True(Assert.IsType<GiftRedemptionReversed>(Assert.Single(gift.DequeueDomainEvents())).FullyUnredeemed);
    }

    [Fact]
    public void Reversing_an_items_redemption_returns_the_units()
    {
        var gift = GiftTests.Delivered();
        var line = gift.Items[0];
        var redemption = gift.Redeem(gift.BusinessId!.Value, false, [new GiftLine(line.CatalogItemId, 1)], null, Guid.CreateVersion7(), Now);
        var remainingBefore = gift.Items[0].RemainingQty;

        gift.ReverseRedemption(redemption, Guid.CreateVersion7(), Now);

        Assert.Equal(remainingBefore + 1, gift.Items[0].RemainingQty);
        Assert.Equal(gift.AmountQepik, gift.RemainingQepik);
    }

    [Fact]
    public void A_reversal_entry_or_another_gifts_redemption_cannot_be_reversed()
    {
        var gift = GiftTests.Delivered(GiftContent.ForBusinessVoucher(Shop(), 10_00));
        var other = GiftTests.Delivered(GiftContent.ForBusinessVoucher(Shop(), 10_00));
        var redemption = gift.Redeem(gift.BusinessId!.Value, false, null, 5_00, Guid.CreateVersion7(), Now);
        var reversal = gift.ReverseRedemption(redemption, Guid.CreateVersion7(), Now);

        Assert.Throws<InvalidOperationException>(() => gift.ReverseRedemption(reversal, Guid.CreateVersion7(), Now));
        Assert.Throws<InvalidOperationException>(() => other.ReverseRedemption(redemption, Guid.CreateVersion7(), Now));
    }

    private static BusinessId Shop() => StronglyTypedId.New<BusinessId>();
}
