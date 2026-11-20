using Qonaqet.Domain.Ledger;

namespace Qonaqet.Domain.Tests.Ledger;

public sealed class LedgerPostingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Online_paid_redemption_credits_the_business_and_moves_seven_percent_to_the_platform()
    {
        var business = Guid.CreateVersion7();
        var entries = LedgerPostings.Redemption(business, 10_00, commissionable: true, "Claim", Guid.CreateVersion7(), Guid.CreateVersion7(), CommissionPolicy.Default, Now);

        Assert.Equal(3, entries.Count);
        Assert.Equal(10_00 - 70, entries.Where(e => e.AccountId == business).Sum(e => e.Amount));
        var platform = Assert.Single(entries, e => e.AccountType == LedgerAccountType.Platform);
        Assert.Equal(70, platform.Amount);
        Assert.Equal(0, entries.Sum(e => e.Amount) - 10_00); // yalnız kredit sistemə daxil olur; komissiya hesablar arası köçürmədir
        Assert.Equal(3, entries.Select(e => e.SourceEventId).Distinct().Count());
    }

    [Fact]
    public void In_person_daily_and_suspended_redemptions_write_nothing()
    {
        Assert.Empty(LedgerPostings.Redemption(Guid.CreateVersion7(), 0, commissionable: false, "Claim", Guid.CreateVersion7(), Guid.CreateVersion7(), CommissionPolicy.Default, Now));
        Assert.Empty(LedgerPostings.Redemption(Guid.CreateVersion7(), 5_00, commissionable: false, "Claim", Guid.CreateVersion7(), Guid.CreateVersion7(), CommissionPolicy.Default, Now));
    }

    [Theory]
    [InlineData(7, 150, 11)] // 10.5 → 11
    [InlineData(7, 100, 7)]
    [InlineData(7, 1, 0)]
    [InlineData(10, 2_99, 30)]
    [InlineData(0, 10_00, 0)]
    public void Commission_is_configurable_and_rounds_half_up(decimal percent, long value, long expected) =>
        Assert.Equal(expected, new CommissionPolicy(percent).CommissionFor(value));

    [Fact]
    public void Source_ids_are_deterministic_per_event_and_leg()
    {
        var eventId = Guid.CreateVersion7();

        Assert.Equal(LedgerEntry.SourceId(eventId, "credit"), LedgerEntry.SourceId(eventId, "credit"));
        Assert.NotEqual(LedgerEntry.SourceId(eventId, "credit"), LedgerEntry.SourceId(eventId, "commission"));
        Assert.NotEqual(LedgerEntry.SourceId(eventId, "credit"), LedgerEntry.SourceId(Guid.CreateVersion7(), "credit"));
    }

    [Fact]
    public void Refund_leaves_the_platform_account_and_zero_entries_are_rejected()
    {
        var refund = LedgerPostings.Refund(3_00, Guid.CreateVersion7(), Guid.CreateVersion7(), Now);

        Assert.Equal(-3_00, refund.Amount);
        Assert.Equal(LedgerEntryType.Refund, refund.Type);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LedgerEntry.Post(LedgerAccount.Platform, LedgerEntryType.Payout, 0, "Payout", Guid.CreateVersion7(), Guid.CreateVersion7(), "x", Now));
    }
}
