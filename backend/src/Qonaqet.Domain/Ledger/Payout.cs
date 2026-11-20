using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Ledger;

public readonly record struct PayoutId(Guid Value) : IStronglyTypedId<PayoutId>
{
    public static PayoutId From(Guid value) => new(value);
}

/// <summary>
/// Biznesə əl ilə edilən bank köçürməsi (Postgres <c>payouts</c>). Admin köçürməni bankda edir və burada qeyd edir; eyni tranzaksiyada biznesin
/// hesabından <c>Payout</c> ledger yazısı (−məbləğ) düşür. Ödəniş edilib-edilməməsi bankdadır, ona görə qeyd dəyişmir və silinmir;
/// səhv qeyd əks ledger yazısı ilə düzəldilir.
/// </summary>
public sealed class Payout : AggregateRoot<PayoutId>
{
    public const int MaxBankReferenceLength = 100;

    private Payout(PayoutId id, Guid businessId, long amount, string bankReference, Guid createdByAdminId, DateTimeOffset at)
        : base(id, at, createdByAdminId)
    {
        BusinessId = Guard.NotEmpty(businessId);
        Amount = Guard.Positive(amount);
        BankReference = Guard.NotNullOrWhiteSpace(bankReference).Trim();
        if (BankReference.Length > MaxBankReferenceLength)
        {
            throw new ArgumentException("Bank referansı çox uzundur.", nameof(bankReference));
        }

        CreatedByAdminId = Guard.NotEmpty(createdByAdminId);
        Raise(new PayoutCreated(Id, BusinessId, Amount, at));
    }

    private Payout()
    {
    }

    public Guid BusinessId { get; private set; }

    /// <summary>Qəpik.</summary>
    public long Amount { get; private set; }

    public string BankReference { get; private set; } = string.Empty;

    public Guid CreatedByAdminId { get; private set; }

    public static Payout Record(Guid businessId, long amount, string bankReference, Guid adminId, DateTimeOffset at) =>
        new(StronglyTypedId.New<PayoutId>(), businessId, amount, bankReference, adminId, at);

    /// <summary>Ledger yazısı: biznesin hesabından çıxış, mənbə = payout-un özü (bir payout → bir yazı).</summary>
    public LedgerEntry ToLedgerEntry() =>
        LedgerEntry.Post(LedgerAccount.Business(BusinessId), LedgerEntryType.Payout, -Amount, nameof(Payout), Id.Value, Id.Value, "payout", CreatedAt);
}

/// <summary>Payout qeydə alındı (E13-də <c>payout.created</c> webhook-u).</summary>
public sealed record PayoutCreated(PayoutId PayoutId, Guid BusinessId, long Amount, DateTimeOffset At) : DomainEvent(At);
