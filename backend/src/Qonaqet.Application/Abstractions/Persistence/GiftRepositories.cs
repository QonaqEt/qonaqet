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
}
