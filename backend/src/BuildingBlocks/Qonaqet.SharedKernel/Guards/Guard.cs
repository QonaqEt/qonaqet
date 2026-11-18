using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Qonaqet.SharedKernel;

/// <summary>
/// İnvariant yoxlamaları. Yalnız <b>proqramçı xətaları</b> üçündür və exception atır;
/// istifadəçi girişinin yoxlanması validator-larda <see cref="Result"/> ilə edilir.
/// </summary>
public static class Guard
{
    public static T NotNull<T>([NotNull] T? value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
        return value;
    }

    public static string NotNullOrWhiteSpace([NotNull] string? value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value;
    }

    public static long NotNegative(long value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value, paramName);
        return value;
    }

    public static long Positive(long value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, paramName);
        return value;
    }

    public static int InRange(int value, int min, int max, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, min, paramName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, max, paramName);
        return value;
    }

    public static Guid NotEmpty(Guid value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Boş Guid qəbul edilmir.", paramName);
        }

        return value;
    }

    public static DateTimeOffset Utc(DateTimeOffset value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Vaxt UTC olmalıdır.", paramName);
        }

        return value;
    }
}
