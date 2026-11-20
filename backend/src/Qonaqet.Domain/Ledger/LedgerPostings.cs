using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Ledger;

/// <summary>
/// Komissiya siyasəti: onlayn ödənilmiş hər təhvildən (hədiyyə, remote, hovuz, kampaniya) faiz; default 7%. Konfiqurasiya
/// <c>Ledger:CommissionPercent</c>-dən gəlir. Yuvarlaqlaşdırma qəpiyə, yarım yuxarı.
/// </summary>
public sealed record CommissionPolicy(decimal Percent)
{
    public static CommissionPolicy Default { get; } = new(7m);

    public long CommissionFor(long valueQepik)
    {
        Guard.NotNegative(valueQepik);
        if (Percent is < 0 or > 100)
        {
            throw new InvalidOperationException("Komissiya faizi 0–100 arasında olmalıdır.");
        }

        return (long)decimal.Round(valueQepik * Percent / 100m, 0, MidpointRounding.AwayFromZero);
    }
}

/// <summary>Hadisələrin ledger yazılarına çevrilməsi (ikili yazı üslubu: hər hərəkətin iki tərəfi).</summary>
public static class LedgerPostings
{
    /// <summary>
    /// Təhvil: biznesə <c>RedemptionCredit</c> (+dəyər). Onlayn ödənişlidirsə əlavə olaraq <c>Commission</c>: biznesdən −komissiya, platformaya
    /// +komissiya. Pulsuz (in-person <c>BusinessDaily</c> / <c>Suspended</c>) təhvildə heç bir yazı yoxdur.
    /// </summary>
    public static IReadOnlyList<LedgerEntry> Redemption(
        Guid businessId, long valueQepik, bool commissionable, string refType, Guid refId, Guid sourceEventId, CommissionPolicy policy, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (!commissionable || valueQepik <= 0)
        {
            return [];
        }

        var business = LedgerAccount.Business(businessId);
        var entries = new List<LedgerEntry>
        {
            LedgerEntry.Post(business, LedgerEntryType.RedemptionCredit, valueQepik, refType, refId, sourceEventId, "credit", at),
        };
        var commission = policy.CommissionFor(valueQepik);
        if (commission > 0)
        {
            entries.Add(LedgerEntry.Post(business, LedgerEntryType.Commission, -commission, refType, refId, sourceEventId, "commission", at));
            entries.Add(LedgerEntry.Post(LedgerAccount.Platform, LedgerEntryType.Commission, commission, refType, refId, sourceEventId, "commission-platform", at));
        }

        return entries;
    }

    /// <summary>Uğurlu refund: pul platformadan ödəyiciyə qayıdır (platforma hesabından çıxış).</summary>
    public static LedgerEntry Refund(long amountQepik, Guid paymentId, Guid sourceEventId, DateTimeOffset at) =>
        LedgerEntry.Post(LedgerAccount.Platform, LedgerEntryType.Refund, -Guard.Positive(amountQepik), "Payment", paymentId, sourceEventId, "refund", at);
}
