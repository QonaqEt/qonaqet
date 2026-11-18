using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Tests.SharedKernel;

public sealed class MoneyTests
{
    [Fact]
    public void Manat_is_converted_to_qepik()
    {
        Assert.Equal(1250, Money.FromManat(12, 50).Qepik);
        Assert.Equal(200, Money.FromManat(2).Qepik);
        Assert.Equal(-1250, Money.FromManat(-12, 50).Qepik);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public void Qepik_part_must_be_between_0_and_99(int qepik)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.FromManat(1, qepik));
    }

    [Fact]
    public void Arithmetic_is_exact()
    {
        var a = Money.FromQepik(1);
        var b = Money.FromQepik(2);

        Assert.Equal(Money.FromQepik(3), a + b);
        Assert.Equal(Money.FromQepik(-1), a - b);
        Assert.Equal(Money.FromQepik(-1), -a);
        Assert.Equal(Money.FromQepik(30), 3 * Money.FromQepik(10));
    }

    [Fact]
    public void Overflow_throws_instead_of_wrapping()
    {
        var max = Money.FromQepik(long.MaxValue);
        var min = Money.FromQepik(long.MinValue);

        Assert.Throws<OverflowException>(() => max + Money.FromQepik(1));
        Assert.Throws<OverflowException>(() => min - Money.FromQepik(1));
        Assert.Throws<OverflowException>(() => -min);
        Assert.Throws<OverflowException>(() => max * 2);
        Assert.Throws<OverflowException>(() => Money.FromManat(long.MaxValue / 10));
        Assert.Throws<OverflowException>(() => min.Abs());
    }

    [Fact]
    public void Negative_amounts_are_supported_for_ledger_corrections()
    {
        var refund = Money.FromQepik(-500);

        Assert.True(refund.IsNegative);
        Assert.False(refund.IsPositive);
        Assert.Equal(Money.FromQepik(500), refund.Abs());
        Assert.True(refund < Money.Zero);
        Assert.Equal("-5.00 AZN", refund.ToString());
    }

    [Theory]
    [InlineData(10_000, 700, 700)]   // 100 AZN × 7% = 7 AZN
    [InlineData(150, 700, 11)]       // 10.5 qəpik → 11 (yarım yuxarı)
    [InlineData(149, 700, 10)]       // 10.43 → 10
    [InlineData(-150, 700, -11)]     // mənfi tərəfdə də sıfırdan uzağa
    [InlineData(1, 700, 0)]
    [InlineData(0, 700, 0)]
    [InlineData(12_345, 0, 0)]
    [InlineData(12_345, 10_000, 12_345)]
    public void Percent_of_rounds_half_away_from_zero(long qepik, int basisPoints, long expected)
    {
        Assert.Equal(expected, Money.FromQepik(qepik).PercentOf(basisPoints).Qepik);
    }

    [Fact]
    public void Percent_of_large_amount_does_not_overflow_in_intermediate_step()
    {
        var large = Money.FromQepik(long.MaxValue);

        Assert.Equal(large, large.PercentOf(10_000));
        Assert.Throws<ArgumentOutOfRangeException>(() => large.PercentOf(-1));
        Assert.Throws<OverflowException>(() => large.PercentOf(20_000));
    }

    [Theory]
    [InlineData("12", 1200)]
    [InlineData("12.5", 1250)]
    [InlineData("12.50", 1250)]
    [InlineData("0.07", 7)]
    [InlineData("-0.07", -7)]
    [InlineData(" 3.00 ", 300)]
    public void Parses_invariant_decimal_text(string text, long expected)
    {
        Assert.True(Money.TryParse(text, out var money));
        Assert.Equal(expected, money.Qepik);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12,50")]
    [InlineData("12.505")]
    [InlineData("12.")]
    [InlineData(".5")]
    [InlineData("1e3")]
    [InlineData("--1")]
    [InlineData("99999999999999999999")]
    public void Rejects_invalid_text(string? text)
    {
        Assert.False(Money.TryParse(text, out var money));
        Assert.Equal(Money.Zero, money);
    }

    [Fact]
    public void Formats_extremes_without_overflow()
    {
        Assert.Equal("0.00 AZN", Money.Zero.ToString());
        Assert.Equal("-92233720368547758.08", Money.FromQepik(long.MinValue).ToDecimalString());
        Assert.Equal("92233720368547758.07", Money.FromQepik(long.MaxValue).ToDecimalString());
    }

    [Fact]
    public void Equality_and_ordering_follow_qepik()
    {
        Assert.Equal(Money.FromManat(1), Money.FromQepik(100));
        Assert.True(Money.FromQepik(99) < Money.FromManat(1));
        Assert.Equal(-1, Money.FromQepik(1).CompareTo(Money.FromQepik(2)));
    }
}
