using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Social;

public readonly record struct PoolDonationId(Guid Value) : IStronglyTypedId<PoolDonationId>
{
    public static PoolDonationId From(Guid value) => new(value);
}

public enum PoolDonationStatus
{
    /// <summary>Ödəniş gözlənilir: hovuz balansına hələ daxil deyil.</summary>
    Pending,

    /// <summary>Ödəniş təsdiqləndi, hovuz balansı artırıldı.</summary>
    Confirmed,

    /// <summary>Ödəniş uğursuz oldu və ya bitdi.</summary>
    Cancelled,
}

/// <summary>
/// Hovuza (kateqoriyanın default hovuzu və ya kampaniya hovuzu) ianə (<c>poolDonations</c>). Adlı ianələrin adı hovuzdan maliyyələşən claim-lərdə
/// proporsional atribusiya ilə göstərilir: claim ən köhnə istifadə olunmamış ianədən maliyyələşir, <see cref="RemainingQepik"/> həmin ianənin
/// hələ xərclənməmiş hissəsidir. Ianəçi alıcıları heç vaxt görmür. Hovuz və kampaniya ianələri geri qaytarılmır.
/// </summary>
public sealed class PoolDonation : AggregateRoot<PoolDonationId>
{
    private PoolDonation(PoolDonationId id, DateTimeOffset createdAt, Guid? createdBy)
        : base(id, createdAt, createdBy)
    {
    }

    private PoolDonation()
    {
    }

    public PoolId PoolId { get; private set; }

    /// <summary>Kampaniya hovuzuna ianədə kampaniya (<c>raisedQepik</c> bu ianə ilə artır).</summary>
    public Guid? CampaignId { get; private set; }

    public Guid? DonorUserId { get; private set; }

    public Guid? CorporateId { get; private set; }

    public long AmountQepik { get; private set; }

    /// <summary>Hələ claim-lərə xərclənməmiş hissə (təsdiqdən sonra = <see cref="AmountQepik"/>).</summary>
    public long RemainingQepik { get; private set; }

    public DonorDisplay DonorDisplay { get; private set; }

    public string? DonorName { get; private set; }

    public Guid? PaymentId { get; private set; }

    public PoolDonationStatus Status { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public bool IsNamed => DonorDisplay == DonorDisplay.Named && DonorName is not null;

    public static PoolDonation Pledge(
        PoolId poolId, Guid? campaignId, long amountQepik, DonorDisplay donorDisplay, string? donorName, Guid? donorUserId, Guid? corporateId, DateTimeOffset at)
    {
        if (!Enum.IsDefined(donorDisplay))
        {
            throw new ArgumentOutOfRangeException(nameof(donorDisplay));
        }

        return new PoolDonation(StronglyTypedId.New<PoolDonationId>(), at, donorUserId)
        {
            PoolId = poolId,
            CampaignId = campaignId,
            AmountQepik = Guard.Positive(amountQepik),
            DonorDisplay = donorDisplay,
            DonorName = donorDisplay == DonorDisplay.Named && !string.IsNullOrWhiteSpace(donorName) ? donorName.Trim() : null,
            DonorUserId = donorUserId,
            CorporateId = corporateId,
            Status = PoolDonationStatus.Pending,
        };
    }

    public void AttachPayment(Guid paymentId)
    {
        if (PaymentId is not null)
        {
            throw new InvalidOperationException("Ödəniş artıq bağlanıb.");
        }

        PaymentId = Guard.NotEmpty(paymentId);
    }

    /// <summary>Ödəniş təsdiqləndi: <c>Pending</c> → <c>Confirmed</c>; təkrar çağırış <c>false</c>.</summary>
    public bool Confirm(DateTimeOffset at)
    {
        if (Status != PoolDonationStatus.Pending)
        {
            return false;
        }

        Status = PoolDonationStatus.Confirmed;
        RemainingQepik = AmountQepik;
        ConfirmedAt = Guard.Utc(at);
        MarkUpdated(at, null);
        return true;
    }
}
