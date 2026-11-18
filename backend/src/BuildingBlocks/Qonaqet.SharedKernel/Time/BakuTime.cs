namespace Qonaqet.SharedKernel;

/// <summary>
/// Bakı iş günü köməkçiləri. Vaxt UTC saxlanır; günlük kvota, hesabat və liderlər cədvəli
/// dövrləri isə <c>Asia/Baku</c> təqvim günü ilə hesablanır.
/// </summary>
public static class BakuTime
{
    public const string TimeZoneId = "Asia/Baku";

    /// <summary>
    /// Sistem saat qurşağı bazası (tzdata / ICU) yoxdursa, 2016-dan bəri qüvvədə olan sabit UTC+4
    /// (yay vaxtı yoxdur) istifadə olunur.
    /// </summary>
    public static TimeZoneInfo TimeZone { get; } = ResolveTimeZone();

    /// <summary>Verilən anın Bakı təqvim günü.</summary>
    public static DateOnly ToBakuDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZone).DateTime);

    /// <summary>Verilən anın Bakı yerli vaxtı.</summary>
    public static DateTimeOffset ToBakuTime(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, TimeZone);

    /// <summary>Bakı gününün başlanğıcı (00:00 yerli vaxt) UTC ilə.</summary>
    public static DateTimeOffset StartOfDayUtc(DateOnly bakuDate)
    {
        var local = bakuDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var offset = TimeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    /// <summary>Bakı gününün sonu (növbəti günün başlanğıcı, eksklüziv) UTC ilə.</summary>
    public static DateTimeOffset EndOfDayUtc(DateOnly bakuDate) => StartOfDayUtc(bakuDate.AddDays(1));

    /// <summary>Saata görə bugünkü Bakı günü.</summary>
    public static DateOnly BakuToday(this IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return ToBakuDate(clock.UtcNow);
    }

    private static TimeZoneInfo ResolveTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone(TimeZoneId, TimeSpan.FromHours(4), "Baku", "Baku");
        }
    }
}
