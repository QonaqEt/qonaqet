using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Social;

namespace Qonaqet.Application.Abstractions.Persistence;

/// <summary><c>offers</c>: sosial qonaqlıq təklifləri.</summary>
public interface IOfferRepository : IRepository<Offer, OfferId>
{
    /// <summary>Biznesin bütün (silinməmiş) təklifləri, ən yenilər əvvəl.</summary>
    Task<IReadOnlyList<Offer>> ListForBusinessAsync(BusinessId businessId, CancellationToken cancellationToken);

    /// <summary>Aktiv təkliflər (ictimai siyahı), filtrlərlə; ən yenilər əvvəl, ən çox <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<Offer>> ListActiveAsync(CategoryId? categoryId, BusinessId? businessId, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Bir vahidi <b>tək şərtli atomik</b> yeniləmə ilə götürür (tranzaksiyadan kənarda, ona görə paralel claim-lər bir-birini gözləyir,
    /// toqquşmur): gündəlik təklifdə <c>usedToday &lt; dailyQuota</c> (başqa gün → lazy sıfırlama ilə 1), askıda/remote-da
    /// <c>remaining &gt; 0</c>. Yalnız aktiv, silinməmiş təklifdə. Vahid yoxdursa <c>false</c> — kvota heç vaxt aşılmır.
    /// </summary>
    Task<bool> TryTakeUnitAsync(Offer offer, string bakuDateKey, CancellationToken cancellationToken);

    /// <summary>
    /// Götürülmüş vahidi geri qaytarır (rezerv buraxılanda, atomik): gündəlik təklifdə yalnız sayğac hələ həmin gündədirsə
    /// (<paramref name="quotaDate"/>), askıda/remote-da <c>remaining + 1</c> (ümumi saydan çox olmadan).
    /// </summary>
    Task ReleaseUnitAsync(OfferId offerId, OfferType type, string? quotaDate, CancellationToken cancellationToken);

    /// <summary>Biznes silinəndə onun bütün təkliflərini <see cref="BusinessCascade.DeleteReason"/> ilə soft delete edir.</summary>
    Task<long> CascadeDeleteForBusinessAsync(BusinessId businessId, Guid? userId, CancellationToken cancellationToken);

    /// <summary>Kaskadla silinmiş təklifləri bərpa edir (sahibin ayrıca sildikləri silinmiş qalır).</summary>
    Task<long> CascadeRestoreForBusinessAsync(BusinessId businessId, Guid? userId, CancellationToken cancellationToken);

    /// <summary>
    /// Remote təklifi ödəniş təsdiqindən sonra aktivləşdirir (atomik, sessiyasız): yalnız hələ <c>awaitingPayment</c>-dirsə. Təkrar çağırış
    /// <c>false</c> qaytarır — consumer idempotentdir.
    /// </summary>
    Task<bool> TryActivateRemoteAsync(OfferId offerId, CancellationToken cancellationToken);

    /// <summary>Ödənilməyən remote təklifi bağlayır (atomik): <c>awaitingPayment</c> → qeyri-aktiv, soft delete (<c>payment_not_completed</c>).</summary>
    Task<bool> TryCloseUnpaidRemoteAsync(OfferId offerId, CancellationToken cancellationToken);

    /// <summary>İstifadəçinin ianəçi olduğu (askıda/remote) təkliflər, ən yenilər əvvəl.</summary>
    Task<IReadOnlyList<Offer>> ListDonatedByAsync(Guid donorUserId, int limit, CancellationToken cancellationToken);
}

/// <summary><c>claims</c>: sosial qonaqlıq rezervləri.</summary>
public interface IClaimRepository : IRepository<Claim, ClaimId>
{
    /// <summary>
    /// Yeni rezervi əlavə edir. İstifadəçinin artıq <c>Reserved</c> claim-i varsa (partial unikal indeks, paralel sorğularda da)
    /// <c>false</c> qaytarır.
    /// </summary>
    Task<bool> TryAddReservedAsync(Claim claim, CancellationToken cancellationToken);

    /// <summary>
    /// Aqreqatı yalnız sənəd hələ <paramref name="expected"/> vəziyyətindədirsə yazır (optimistik status keçidi); başqası artıq
    /// dəyişibsə <c>false</c>. İki instans/işçi eyni claim-i eyni anda emal edə bilməz.
    /// </summary>
    Task<bool> TryTransitionAsync(Claim claim, ClaimStatus expected, CancellationToken cancellationToken);

    /// <summary>İstifadəçinin hazırkı <c>Reserved</c> claim-i (müddəti keçmiş olsa da); yoxdursa <c>null</c>.</summary>
    Task<Claim?> FindReservedForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Rezervi bağlayır (<c>Expired</c>/<c>Cancelled</c>): yalnız sənəd hələ <c>Reserved</c>-dirsə, <b>tranzaksiyadan kənarda</b> atomik.
    /// Qalib gələn tək çağırış <c>true</c> alır və vahidi o qaytarır — rezerv heç vaxt iki dəfə buraxılmır.
    /// </summary>
    Task<bool> TryCloseReservationAsync(Claim claim, CancellationToken cancellationToken);

    /// <summary>
    /// İstifadəçinin <paramref name="since"/>-dən bəri yaratdığı və hələ "sayılan" (<c>Reserved</c> və ya <c>Redeemed</c>) claim-ləri;
    /// <paramref name="offerId"/> verilibsə yalnız həmin təklifdən. Bitmiş və ləğv olunmuş rezervlər limitə daxil deyil.
    /// </summary>
    Task<long> CountCountedSinceAsync(Guid userId, DateTimeOffset since, OfferId? offerId, CancellationToken cancellationToken);

    /// <summary>Biznesdə qısa kodun hash-inə uyğun ən son claim (istənilən statusda, ki təkrar təhvil <c>already_redeemed</c> alsın).</summary>
    Task<Claim?> FindByShortCodeAsync(BusinessId businessId, string shortCodeHash, CancellationToken cancellationToken);

    /// <summary>Biznesin bütün <c>Reserved</c> claim-ləri (biznes silinəndə ləğv üçün).</summary>
    Task<IReadOnlyList<Claim>> ListReservedForBusinessAsync(BusinessId businessId, CancellationToken cancellationToken);

    /// <summary>İstifadəçinin bütün <c>Reserved</c> claim-ləri (bloklama zamanı ləğv üçün).</summary>
    Task<IReadOnlyList<Claim>> ListReservedForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Müddəti keçmiş, hələ <c>Reserved</c> qalan claim-lər (ən köhnələr əvvəl), ən çox <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<Claim>> ListStaleReservedAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);

    /// <summary>Təklifdən neçə claim təhvil verilib (ianəçinin aqreqatı üçün; kim olduğu yox, yalnız say).</summary>
    Task<long> CountRedeemedForOfferAsync(OfferId offerId, CancellationToken cancellationToken);

    /// <summary>Alıcının claim-ləri, ən yenilər əvvəl, ən çox <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<Claim>> ListForRecipientAsync(Guid userId, int limit, CancellationToken cancellationToken);
}

/// <summary><c>poolDonations</c>: hovuz və kampaniya ianələri.</summary>
public interface IPoolDonationRepository : IRepository<PoolDonation, PoolDonationId>
{
    /// <summary>
    /// Ödəniş təsdiqi: bir Mongo tranzaksiyasında ianə <c>Pending</c> → <c>Confirmed</c> və hovuzun balansı (kampaniya ianəsində kampaniyanın
    /// <c>raisedQepik</c>-i də) artır. Paralel tranzaksiya toqquşmasında təkrarlanır. Ianə artıq təsdiqlənibsə heç nə dəyişmir. Ianə (sonrakı
    /// vəziyyəti ilə) qaytarılır; tapılmasa <c>null</c>.
    /// </summary>
    Task<PoolDonation?> ConfirmAsync(PoolDonationId id, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>Ödənilməyən ianəni bağlayır (yalnız <c>Pending</c>-dən, atomik).</summary>
    Task<bool> TryCancelPendingAsync(PoolDonationId id, CancellationToken cancellationToken);

    /// <summary>Ianəçinin ianələri, ən yenilər əvvəl.</summary>
    Task<IReadOnlyList<PoolDonation>> ListForDonorAsync(Guid donorUserId, int limit, CancellationToken cancellationToken);
}
