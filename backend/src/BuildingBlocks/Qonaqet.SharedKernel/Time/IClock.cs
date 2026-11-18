namespace Qonaqet.SharedKernel;

/// <summary>
/// Cari vaxt mənbəyi. Kod <see cref="DateTimeOffset.UtcNow"/>-u birbaşa çağırmır, bunu işlədir ki,
/// testlər vaxtı idarə edə bilsin. Həmişə UTC qaytarır.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary><see cref="TimeProvider"/> üzərində real saat.</summary>
public sealed class SystemClock(TimeProvider timeProvider) : IClock
{
    public SystemClock()
        : this(TimeProvider.System)
    {
    }

    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
}
