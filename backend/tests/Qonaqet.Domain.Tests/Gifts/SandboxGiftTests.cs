using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Tests.Businesses;

namespace Qonaqet.Domain.Tests.Gifts;

public sealed class SandboxGiftTests
{
    private static readonly DateTimeOffset Now = GiftTests.Now;

    private static Gift TestVoucher()
    {
        var gift = Gift.CreateSandboxTestVoucher(GiftTests.Shop, 30_00, Guid.CreateVersion7(), "Test POS", 60, Now);
        gift.ConfirmPaid(new GiftLinkSecret("hash", "sealed"), Now);
        gift.Deliver(null, Now);
        gift.DequeueDomainEvents();
        return gift;
    }

    [Fact]
    public void Test_voucher_is_sandbox_and_not_tied_to_the_partner_balance()
    {
        var gift = TestVoucher();

        Assert.True(gift.IsSandbox);
        Assert.Null(gift.PartnerClientId);
        Assert.Equal((GiftStatus.Delivered, 30_00L), (gift.Status, gift.RemainingQepik));
    }

    [Fact]
    public void Redemption_and_reversal_events_carry_the_sandbox_flag()
    {
        var gift = TestVoucher();

        var redemption = gift.Redeem(GiftTests.Shop, false, null, 10_00, Guid.CreateVersion7(), Now);
        Assert.True(Assert.IsType<GiftRedeemed>(Assert.Single(gift.DequeueDomainEvents())).Sandbox);

        gift.ReverseRedemption(redemption, Guid.CreateVersion7(), Now.AddMinutes(1));
        Assert.True(Assert.IsType<GiftRedemptionReversed>(Assert.Single(gift.DequeueDomainEvents())).Sandbox);
    }

    [Fact]
    public void Expired_test_voucher_returns_nothing_to_anyone()
    {
        var gift = TestVoucher();

        gift.Expire(gift.ExpiresAt);

        Assert.IsType<GiftExpired>(Assert.Single(gift.DequeueDomainEvents()));
    }

    [Fact]
    public void Business_sandbox_flag_changes_only_when_different()
    {
        var business = Business.Submit(BusinessTests.Profile(), "test-kafe", Guid.CreateVersion7(), Now);

        Assert.False(business.IsSandbox);
        Assert.True(business.SetSandbox(true, Now, null));
        Assert.False(business.SetSandbox(true, Now, null));
        Assert.True(business.IsSandbox);
    }
}
