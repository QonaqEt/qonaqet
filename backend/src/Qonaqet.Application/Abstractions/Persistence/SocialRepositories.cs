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
}
