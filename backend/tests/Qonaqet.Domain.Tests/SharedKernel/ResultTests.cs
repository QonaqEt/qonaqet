using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Tests.SharedKernel;

public sealed class ResultTests
{
    private static readonly Error SampleError = Error.NotFound("gift_not_found", "Hədiyyə tapılmadı.");

    [Fact]
    public void Success_has_no_error()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void Failure_carries_the_error_without_throwing()
    {
        Result result = SampleError;

        Assert.True(result.IsFailure);
        Assert.Equal("gift_not_found", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public void Failure_with_none_is_a_programming_error()
    {
        Assert.Throws<ArgumentException>(() => Result.Failure(Error.None));
        Assert.Throws<ArgumentException>(() => Result<int>.Failure(Error.None));
    }

    [Fact]
    public void Value_of_success_is_returned()
    {
        Result<int> result = 42;

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(42, value);
    }

    [Fact]
    public void Value_of_failure_throws_and_try_get_returns_false()
    {
        Result<int> result = SampleError;

        var ex = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains("gift_not_found", ex.Message, StringComparison.Ordinal);
        Assert.False(result.TryGetValue(out _));
    }

    [Fact]
    public void Map_and_bind_short_circuit_on_failure()
    {
        var calls = 0;
        Result<int> failure = SampleError;

        var mapped = failure.Map(v => { calls++; return v * 2; });
        var bound = failure.Bind(v => { calls++; return Result<string>.Success(v.ToString(System.Globalization.CultureInfo.InvariantCulture)); });

        Assert.Equal(0, calls);
        Assert.Equal(SampleError, mapped.Error);
        Assert.Equal(SampleError, bound.Error);
    }

    [Fact]
    public void Map_bind_and_match_apply_on_success()
    {
        Result<int> success = 21;

        var doubled = success.Map(v => v * 2);
        var checkedValue = doubled.Bind(v => v > 40 ? Result<string>.Success("big") : Error.Validation("too_small", "Kiçikdir."));

        Assert.Equal(42, doubled.Value);
        Assert.Equal("big", checkedValue.Value);
        Assert.Equal("ok:21", success.Match(v => $"ok:{v}", e => e.Code));
    }

    [Fact]
    public void Error_requires_a_code()
    {
        Assert.Throws<ArgumentException>(() => Error.Validation(" ", "boş kod"));
    }

    [Fact]
    public void Errors_with_same_values_are_equal()
    {
        Assert.Equal(Error.Conflict("gift_changed", "x"), Error.Conflict("gift_changed", "x"));
        Assert.NotEqual(Error.Conflict("gift_changed", "x"), Error.Validation("gift_changed", "x"));
    }
}
