using System.Security.Cryptography;
using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Ledger;

public enum LedgerAccountType
{
    Business,
    Pool,
    Corporate,
    PartnerClient,
    Platform,
}

public enum LedgerEntryType
{
    PoolDeposit,
    PoolReserve,
    PoolRelease,
    RedemptionCredit,
    Commission,
    Payout,
    CampaignRollover,
    GiftExpiryDonation,
    Refund,
    PartnerTopUp,
    PartnerIssue,
}

/// <summary>Ledger hesabı. Platformanın tək hesabı var: <see cref="Platform"/> (ID = <see cref="Guid.Empty"/>).</summary>
public readonly record struct LedgerAccount(LedgerAccountType Type, Guid Id)
{
    public static LedgerAccount Platform { get; } = new(LedgerAccountType.Platform, Guid.Empty);

    public static LedgerAccount Business(Guid businessId) => new(LedgerAccountType.Business, Guard.NotEmpty(businessId));

    public static LedgerAccount Pool(Guid poolId) => new(LedgerAccountType.Pool, Guard.NotEmpty(poolId));
}

/// <summary>
/// Ledger yazısı (Postgres <c>ledger_entries</c>, append-only): heç vaxt dəyişmir və silinmir, düzəliş yalnız <b>əks yazı</b> ilədir.
/// Hesabın balansı <c>SUM(amount)</c>-dır. <see cref="SourceEventId"/> unikaldır: eyni hadisənin (və eyni "ayağının") ikinci emalı yazı yaratmır.
/// </summary>
public sealed class LedgerEntry : IAppendOnly
{
    private LedgerEntry(
        LedgerAccount account, LedgerEntryType type, long amount, string refType, Guid refId, Guid sourceEventId, DateTimeOffset createdAt)
    {
        if (amount == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Sıfır ledger yazısı olmur.");
        }

        Id = Guid.CreateVersion7();
        AccountType = account.Type;
        AccountId = account.Id;
        Type = type;
        Amount = amount;
        RefType = Guard.NotNullOrWhiteSpace(refType);
        RefId = refId;
        SourceEventId = Guard.NotEmpty(sourceEventId);
        CreatedAt = Guard.Utc(createdAt);
    }

    private LedgerEntry()
    {
    }

    public Guid Id { get; private set; }

    public LedgerAccountType AccountType { get; private set; }

    public Guid AccountId { get; private set; }

    public LedgerEntryType Type { get; private set; }

    /// <summary>İşarəli qəpik: hesaba daxil olan müsbət, çıxan mənfi.</summary>
    public long Amount { get; private set; }

    /// <summary>Mənbə obyektin tipi (<c>Claim</c>, <c>Gift</c>, <c>Payment</c>, <c>Payout</c> …).</summary>
    public string RefType { get; private set; } = string.Empty;

    public Guid RefId { get; private set; }

    public Guid SourceEventId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public LedgerAccount Account => new(AccountType, AccountId);

    /// <summary>Yazı; <paramref name="leg"/> bir hadisənin bir neçə yazısını (kredit, komissiya …) fərqləndirir.</summary>
    public static LedgerEntry Post(
        LedgerAccount account, LedgerEntryType type, long amount, string refType, Guid refId, Guid sourceEventId, string leg, DateTimeOffset at) =>
        new(account, type, amount, refType, refId, SourceId(sourceEventId, leg), at);

    /// <summary>
    /// Hadisə + ayaq → sabit (deterministik) ID. Eyni hadisə təkrar emal olunsa eyni ID çıxır və unikal indeks ikinci yazını kəsir.
    /// </summary>
    public static Guid SourceId(Guid eventId, string leg)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leg);
        Span<byte> input = stackalloc byte[16 + (leg.Length * 3)];
        eventId.TryWriteBytes(input);
        var written = System.Text.Encoding.UTF8.GetBytes(leg, input[16..]);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input[..(16 + written)], hash);
        var bytes = hash[..16];
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x80); // UUID v8 (xüsusi), RFC 9562
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}
