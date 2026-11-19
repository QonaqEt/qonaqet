using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Catalog;
using Qonaqet.Domain.Categories;
using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Social;

public readonly record struct ClaimId(Guid Value) : IStronglyTypedId<ClaimId>
{
    public static ClaimId From(Guid value) => new(value);
}

public enum ClaimSource
{
    Offer,
    Pool,
}

public enum ClaimStatus
{
    Reserved,
    Redeemed,
    Expired,
    Cancelled,
}

/// <summary>
/// Sosial qonaqlıqda alıcının rezervi (<c>claims</c>). Alıcını heç kim seçmir: uyğun istifadəçi özü claim edir. Biznes yalnız
/// məhsulu, statusu və məbləği görür; həssas kateqoriyada kateqoriya adı əvəzinə "Qonaq" göstərilir. Rezerv 30 dəqiqə
/// qüvvədədir (məntiqi müddət, <see cref="ExpiresAt"/>); təhvil verilməyən rezervi job buraxır.
/// </summary>
public sealed class Claim : AggregateRoot<ClaimId>
{
    public static readonly TimeSpan ReservationTtl = TimeSpan.FromMinutes(30);

    private Claim(ClaimId id, DateTimeOffset createdAt, Guid? createdBy)
        : base(id, createdAt, createdBy)
    {
    }

    private Claim()
    {
    }

    public Guid RecipientUserId { get; private set; }

    public BusinessId BusinessId { get; private set; }

    public ClaimSource Source { get; private set; }

    public OfferId? OfferId { get; private set; }

    /// <summary>Təklifin növü (snapshot): komissiya və ledger qərarı üçün (<c>BusinessDaily</c>/<c>Suspended</c> pulsuzdur).</summary>
    public OfferType? OfferType { get; private set; }

    public PoolId? PoolId { get; private set; }

    public CatalogItemId? CatalogItemId { get; private set; }

    /// <summary>Pullu mənbədə (pool/remote) dəyər, qəpiklə; pulsuz təkliflərdə <c>null</c>.</summary>
    public long? AmountQepik { get; private set; }

    public CategoryId CategoryId { get; private set; }

    /// <summary>Məhsulun adı (snapshot) — işçiyə göstərilən yeganə "nə" məlumatı.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>Alıcıya göstərilən mətn: "Leyla sizə ısmarladı 💚" və ya "Bir xeyirxah insan sizə ısmarladı 💚".</summary>
    public string DonorDisplayText { get; private set; } = string.Empty;

    public ClaimStatus Status { get; private set; }

    /// <summary>Gündəlik təklifdə vahidin götürüldüyü Bakı günü (<c>yyyy-MM-dd</c>): rezerv buraxılanda yalnız həmin günün sayğacı azalır.</summary>
    public string? QuotaDate { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RedeemedAt { get; private set; }

    public Guid? RedeemedByStaffId { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancelReason { get; private set; }

    /// <summary>Qısa kodun hash-i (kodun özü saxlanılmır; QR və qısa kod ID-dən server sirri ilə törədilir).</summary>
    public string? ShortCodeHash { get; private set; }

    public bool IsReserved => Status == ClaimStatus.Reserved;

    /// <summary>Rezerv bu anda hələ qüvvədədirmi (müddət məntiqidir).</summary>
    public bool IsRedeemableAt(DateTimeOffset now) => Status == ClaimStatus.Reserved && now < ExpiresAt;

    /// <summary>Təklifdən rezerv: vahid artıq atomik götürülüb (<paramref name="quotaDate"/> gündəlik təklifdə həmin gündür).</summary>
    public static Claim ReserveFromOffer(Offer offer, Guid recipientUserId, string donorDisplayText, string? quotaDate, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(offer);
        Guard.Utc(now);
        return new Claim(StronglyTypedId.New<ClaimId>(), now, recipientUserId)
        {
            RecipientUserId = Guard.NotEmpty(recipientUserId),
            BusinessId = offer.BusinessId,
            Source = ClaimSource.Offer,
            OfferId = offer.Id,
            OfferType = offer.Type,
            CatalogItemId = offer.CatalogItemId,
            CategoryId = offer.CategoryId,
            Title = offer.Title,
            DonorDisplayText = Guard.NotNullOrWhiteSpace(donorDisplayText),
            Status = ClaimStatus.Reserved,
            QuotaDate = offer.Type == Social.OfferType.BusinessDaily ? Guard.NotNullOrWhiteSpace(quotaDate) : null,
            ExpiresAt = now + ReservationTtl,
        };
    }

    /// <summary>Qısa kodun hash-ini bağlayır (yaradılandan dərhal sonra, yazılmazdan əvvəl; bir dəfə).</summary>
    public void AttachShortCode(string shortCodeHash)
    {
        if (ShortCodeHash is not null)
        {
            throw new InvalidOperationException("Qısa kod artıq bağlanıb.");
        }

        ShortCodeHash = Guard.NotNullOrWhiteSpace(shortCodeHash);
    }

    /// <summary>Rezerv bitdi (job): <see cref="ClaimStatus.Reserved"/> → <see cref="ClaimStatus.Expired"/>.</summary>
    public void Expire(DateTimeOffset now)
    {
        EnsureReserved();
        Status = ClaimStatus.Expired;
        MarkUpdated(now, null);
    }

    /// <summary>Alıcı, admin və ya sistem (bloklama, biznesin silinməsi) rezervi ləğv edir.</summary>
    public void Cancel(string reason, DateTimeOffset now, Guid? by)
    {
        EnsureReserved();
        Status = ClaimStatus.Cancelled;
        CancelledAt = now;
        CancelReason = Guard.NotNullOrWhiteSpace(reason);
        MarkUpdated(now, by);
    }

    private void EnsureReserved()
    {
        if (Status != ClaimStatus.Reserved)
        {
            throw new InvalidOperationException($"Claim {Status} vəziyyətindədir, rezerv deyil.");
        }
    }
}
