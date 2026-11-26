using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Gifts;

namespace Qonaqet.Application.Abstractions.Persistence;

/// <summary><c>gifts</c>: şəxsə ünvanlanmış hədiyyələr.</summary>
public interface IGiftRepository : IRepository<Gift, GiftId>
{
    /// <summary>
    /// Aqreqatı yalnız sənəd hələ <paramref name="expectedVersion"/> versiyasındadırsa yazır (optimistik konkurensiya; açıq tranzaksiya varsa
    /// onun içində). Başqası artıq dəyişibsə <c>false</c>.
    /// </summary>
    Task<bool> TryReplaceAsync(Gift gift, long expectedVersion, CancellationToken cancellationToken);

    /// <summary>Link token-inin hash-i ilə (silinməmiş) hədiyyə.</summary>
    Task<Gift?> FindByLinkHashAsync(string linkTokenHash, CancellationToken cancellationToken);

    /// <summary>Qısa kodun hash-i ilə hədiyyə.</summary>
    Task<Gift?> FindByShortCodeAsync(string shortCodeHash, CancellationToken cancellationToken);

    /// <summary>Çatdırılma vaxtı çatmış <c>Scheduled</c> hədiyyələr, ən köhnə <c>deliverAt</c> əvvəl.</summary>
    Task<IReadOnlyList<Gift>> ListDueForDeliveryAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);

    /// <summary>Refundu hələ tamamlanmamış hədiyyələr (<c>refundDueQepik &gt; 0</c>), ən köhnələr əvvəl.</summary>
    Task<IReadOnlyList<Gift>> ListRefundDueAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Müddəti bitmiş, hələ yekunlaşmamış (Scheduled və ya alıcının əlində olan) hədiyyələr, ən köhnə <c>expiresAt</c> əvvəl.</summary>
    Task<IReadOnlyList<Gift>> ListDueForExpiryAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);

    /// <summary>Müddətinin bitməsinə <paramref name="until"/>-ə qədər qalan, xatırlatması hələ getməmiş, alıcının əlində olan hədiyyələr.</summary>
    Task<IReadOnlyList<Gift>> ListDueForReminderAsync(DateTimeOffset now, DateTimeOffset until, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Korporativ fan-out paketini yazır: (<c>corporateOrderId</c>, <c>corporateRecipientIndex</c>) artıq varsa həmin hədiyyə atlanır (təkrar
    /// cəhd dublikat yaratmır). Yeni yazılanların sayı.
    /// </summary>
    Task<int> AddCorporateBatchAsync(IReadOnlyList<Gift> gifts, CancellationToken cancellationToken);

    /// <summary>Korporativ sifarişin bütün hədiyyələri (hesabat üçün).</summary>
    Task<IReadOnlyList<Gift>> ListForCorporateOrderAsync(Guid corporateOrderId, CancellationToken cancellationToken);

    /// <summary>Göndərənin hədiyyələri, ən yenilər əvvəl.</summary>
    Task<IReadOnlyList<Gift>> ListSentAsync(Guid senderUserId, int limit, CancellationToken cancellationToken);

    /// <summary>Alıcının (hesabına bağlı) çatdırılmış hədiyyələri, ən yenilər əvvəl.</summary>
    Task<IReadOnlyList<Gift>> ListReceivedAsync(Guid recipientUserId, int limit, CancellationToken cancellationToken);
}

/// <summary><c>giftRedemptions</c> (append-only).</summary>
public interface IGiftRedemptionRepository
{
    Task AddAsync(GiftRedemption redemption, CancellationToken cancellationToken);

    Task<IReadOnlyList<GiftRedemption>> ListForGiftAsync(GiftId giftId, CancellationToken cancellationToken);

    Task<GiftRedemption?> GetAsync(GiftRedemptionId id, CancellationToken cancellationToken);

    /// <summary>Bu təhvilin tərs yazısı varmı (artıq geri alınıb).</summary>
    Task<bool> IsReversedAsync(GiftRedemptionId id, CancellationToken cancellationToken);

    /// <summary>Biznesdəki təhvillər (tərs yazılar daxil) <c>[from, to]</c> aralığında, ən yenilər əvvəl, ən çox <paramref name="limit"/>; və ümumi say.</summary>
    Task<(IReadOnlyList<GiftRedemption> Items, long Total)> ListAtBusinessAsync(
        BusinessId businessId, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken);
}
