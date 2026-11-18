using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Tests.SharedKernel;

public sealed class GuardTests
{
    [Fact]
    public void Invalid_arguments_throw_with_parameter_name()
    {
        string? name = null;
        long amount = -1;

        Assert.Equal("name", Assert.Throws<ArgumentNullException>(() => Guard.NotNull(name)).ParamName);
        Assert.Equal("amount", Assert.Throws<ArgumentOutOfRangeException>(() => Guard.NotNegative(amount)).ParamName);
        Assert.Throws<ArgumentException>(() => Guard.NotNullOrWhiteSpace("  "));
        Assert.Throws<ArgumentOutOfRangeException>(() => Guard.Positive(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Guard.InRange(11, 1, 10));
        Assert.Throws<ArgumentException>(() => Guard.NotEmpty(Guid.Empty));
        Assert.Throws<ArgumentException>(() => Guard.Utc(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(4))));
    }

    [Fact]
    public void Valid_arguments_are_returned()
    {
        var id = Guid.NewGuid();

        Assert.Equal("x", Guard.NotNullOrWhiteSpace("x"));
        Assert.Equal(0, Guard.NotNegative(0));
        Assert.Equal(5, Guard.InRange(5, 1, 10));
        Assert.Equal(id, Guard.NotEmpty(id));
    }
}
