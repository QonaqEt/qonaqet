using System.Globalization;

namespace Qonaqet.SharedKernel;

/// <summary>
/// AZN məbləği, <b>qəpik</b> ilə <see cref="long"/> üzərində (1 AZN = 100 qəpik).
/// İşarəlidir: ledger düzəlişləri və geri qaytarmalar mənfi ola bilər. Bütün hesablamalar
/// <c>checked</c>-dir (overflow → <see cref="OverflowException"/>); <see cref="double"/> /
/// <see cref="decimal"/> ilə çevrilmə yoxdur.
/// </summary>
public readonly struct Money : IEquatable<Money>, IComparable<Money>
{
    public const long QepikPerManat = 100;

    /// <summary>100% = 10 000 bazis punktu.</summary>
    public const int BasisPointsPerWhole = 10_000;

    public static readonly Money Zero;

    private Money(long qepik) => Qepik = qepik;

    public long Qepik { get; }

    public bool IsZero => Qepik == 0;

    public bool IsPositive => Qepik > 0;

    public bool IsNegative => Qepik < 0;

    public static Money FromQepik(long qepik) => new(qepik);

    public static Money FromManat(long manat) => new(checked(manat * QepikPerManat));

    public static Money FromManat(long manat, int qepik)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(qepik);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(qepik, (int)QepikPerManat);

        var whole = checked(manat * QepikPerManat);
        return new Money(manat < 0 ? checked(whole - qepik) : checked(whole + qepik));
    }

    /// <summary>
    /// Məbləğin faizi, bazis punktu ilə (700 = 7%). Yarım qəpik sıfırdan uzağa yuvarlaqlaşdırılır,
    /// ara hesablama <see cref="Int128"/>-dədir, ona görə nəticə sığırsa overflow olmur.
    /// </summary>
    public Money PercentOf(int basisPoints)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(basisPoints);

        var product = (Int128)Qepik * basisPoints;
        var quotient = product / BasisPointsPerWhole;
        var remainder = product % BasisPointsPerWhole;

        if (Int128.Abs(remainder) * 2 >= BasisPointsPerWhole)
        {
            quotient += Int128.Sign(product);
        }

        return new Money(checked((long)quotient));
    }

    public Money Abs() => new(checked(Math.Abs(Qepik)));

    public static Money operator +(Money left, Money right) => new(checked(left.Qepik + right.Qepik));

    public static Money operator -(Money left, Money right) => new(checked(left.Qepik - right.Qepik));

    public static Money operator -(Money value) => new(checked(-value.Qepik));

    public static Money operator *(Money value, long quantity) => new(checked(value.Qepik * quantity));

    public static Money operator *(long quantity, Money value) => value * quantity;

    public static bool operator ==(Money left, Money right) => left.Equals(right);

    public static bool operator !=(Money left, Money right) => !left.Equals(right);

    public static bool operator <(Money left, Money right) => left.Qepik < right.Qepik;

    public static bool operator >(Money left, Money right) => left.Qepik > right.Qepik;

    public static bool operator <=(Money left, Money right) => left.Qepik <= right.Qepik;

    public static bool operator >=(Money left, Money right) => left.Qepik >= right.Qepik;

    public static Money Add(Money left, Money right) => left + right;

    public static Money Subtract(Money left, Money right) => left - right;

    public static Money Negate(Money value) => -value;

    public static Money Multiply(Money value, long quantity) => value * quantity;

    /// <summary>
    /// <c>"12"</c>, <c>"12.5"</c>, <c>"12.50"</c>, <c>"-0.07"</c> formatlarını oxuyur (nöqtə ilə, ən çox 2 onluq).
    /// Mədəniyyətdən asılı deyil və kəsr ədəd tipi işlətmir.
    /// </summary>
    public static bool TryParse(string? text, out Money money)
    {
        money = Zero;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var span = text.AsSpan().Trim();
        var negative = span[0] == '-';
        if (negative)
        {
            span = span[1..];
        }

        var dot = span.IndexOf('.');
        var wholePart = dot < 0 ? span : span[..dot];
        var fractionPart = dot < 0 ? ReadOnlySpan<char>.Empty : span[(dot + 1)..];

        if (wholePart.IsEmpty || fractionPart.Length > 2 || (dot >= 0 && fractionPart.IsEmpty)
            || !IsDigits(wholePart) || !IsDigits(fractionPart)
            || !long.TryParse(wholePart, NumberStyles.None, CultureInfo.InvariantCulture, out var manat))
        {
            return false;
        }

        var qepik = 0;
        foreach (var c in fractionPart)
        {
            qepik = (qepik * 10) + (c - '0');
        }

        if (fractionPart.Length == 1)
        {
            qepik *= 10;
        }

        try
        {
            var total = checked((manat * QepikPerManat) + qepik);
            money = new Money(negative ? -total : total);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public bool Equals(Money other) => Qepik == other.Qepik;

    public override bool Equals(object? obj) => obj is Money other && Equals(other);

    public override int GetHashCode() => Qepik.GetHashCode();

    public int CompareTo(Money other) => Qepik.CompareTo(other.Qepik);

    /// <summary>İnvariant format: <c>12.50 AZN</c>, <c>-0.07 AZN</c>.</summary>
    public override string ToString() => $"{ToDecimalString()} AZN";

    /// <summary>İnvariant format valyutasız: <c>12.50</c>.</summary>
    public string ToDecimalString()
    {
        var sign = Qepik < 0 ? "-" : string.Empty;
        var abs = Qepik == long.MinValue ? (ulong)long.MaxValue + 1 : (ulong)Math.Abs(Qepik);
        var manat = abs / QepikPerManat;
        var qepik = abs % QepikPerManat;
        return string.Create(CultureInfo.InvariantCulture, $"{sign}{manat}.{qepik:00}");
    }

    private static bool IsDigits(ReadOnlySpan<char> span)
    {
        foreach (var c in span)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
