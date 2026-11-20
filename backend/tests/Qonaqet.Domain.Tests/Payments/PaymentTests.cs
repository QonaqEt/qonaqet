using Qonaqet.Domain.Payments;

namespace Qonaqet.Domain.Tests.Payments;

public sealed class PaymentTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    private static Payment NewPayment(long amount = 10_00) =>
        Payment.Create(PaymentPurpose.Gift, Guid.CreateVersion7(), amount, "Fake", "o-" + Guid.NewGuid(), new Uri("https://pay/x"), "Hədiyyə", new PaymentPayer(Guid.CreateVersion7()), Now);

    [Fact]
    public void New_payment_is_pending_with_a_server_amount()
    {
        var payment = NewPayment(25_00);

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(25_00, payment.Amount);
        Assert.Equal(0, payment.RefundableAmount);
        Assert.Throws<ArgumentOutOfRangeException>(() => NewPayment(0));
    }

    [Fact]
    public void Confirmation_is_idempotent_and_raises_one_event()
    {
        var payment = NewPayment();

        Assert.True(payment.MarkPaid(Now.AddMinutes(1), "PAID"));
        Assert.False(payment.MarkPaid(Now.AddMinutes(2), "PAID"));

        var confirmed = Assert.IsType<PaymentConfirmed>(Assert.Single(payment.DequeueDomainEvents()));
        Assert.Equal(payment.Amount, confirmed.Amount);
        Assert.Equal(Now.AddMinutes(1), payment.PaidAt);
    }

    [Fact]
    public void Failed_and_expired_only_leave_pending_but_late_confirmation_still_wins()
    {
        var payment = NewPayment();
        Assert.True(payment.MarkExpired(Now, "CREATED"));
        Assert.False(payment.MarkFailed(Now, "DECLINED"));
        Assert.Equal(PaymentStatus.Expired, payment.Status);

        // Pul provayderdə gec tutulubsa ödəniş yenə təsdiqlənir (məqsədin consumer-i qərar verir).
        Assert.True(payment.MarkPaid(Now.AddMinutes(40), "PAID"));
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.False(payment.MarkExpired(Now, "x"));
    }

    [Fact]
    public void Refunds_move_through_partial_to_full_and_cannot_exceed_the_amount()
    {
        var payment = NewPayment(10_00);
        payment.MarkPaid(Now, "PAID");

        payment.ApplyRefund(RefundId.From(Guid.CreateVersion7()), 3_00, Now);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(7_00, payment.RefundableAmount);

        Assert.Throws<InvalidOperationException>(() => payment.ApplyRefund(RefundId.From(Guid.CreateVersion7()), 7_01, Now));
        payment.ApplyRefund(RefundId.From(Guid.CreateVersion7()), 7_00, Now);

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(10_00, payment.RefundedAmount);
        Assert.Equal(0, payment.RefundableAmount);
    }

    [Fact]
    public void Every_change_bumps_the_version()
    {
        var payment = NewPayment();
        var start = payment.Version;

        payment.MarkPaid(Now, "PAID");
        payment.NoteRefundRequested(Now);

        Assert.Equal(start + 2, payment.Version);
    }

    [Fact]
    public void Refund_lifecycle()
    {
        var refund = Refund.Request(PaymentId.From(Guid.CreateVersion7()), 100, "  səbəb ", null, Now);
        Assert.Equal("səbəb", refund.Reason);
        refund.Succeed("r-1", Now);
        Assert.Equal(RefundStatus.Succeeded, refund.Status);
        Assert.Throws<InvalidOperationException>(() => refund.Fail("x", Now));
    }
}
