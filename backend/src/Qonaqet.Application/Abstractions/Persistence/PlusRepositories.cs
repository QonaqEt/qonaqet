using Qonaqet.Domain.Plus;

namespace Qonaqet.Application.Abstractions.Persistence;

/// <summary><c>brands</c>.</summary>
public interface IBrandRepository : IRepository<Brand, BrandId>
{
    /// <summary>Brendlər ada görə.</summary>
    Task<IReadOnlyList<Brand>> ListAsync(int limit, CancellationToken cancellationToken);
}

/// <summary><c>coupons</c>.</summary>
public interface ICouponRepository : IRepository<Coupon, CouponId>
{
    /// <summary>Kuponlar (brendə görə süzülə bilər), ən yenilər əvvəl.</summary>
    Task<IReadOnlyList<Coupon>> ListAsync(BrandId? brandId, int limit, CancellationToken cancellationToken);

    /// <summary>Hazırda aktiv və etibarlı kuponlar, tezliklə bitənlər əvvəl.</summary>
    Task<IReadOnlyList<Coupon>> ListAvailableAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);
}

/// <summary><c>couponCodes</c>: <c>Unique</c> kuponların kodları.</summary>
public interface ICouponCodeRepository : IRepository<CouponCode, CouponCodeId>
{
    /// <summary>Kodları əlavə edir; kuponda artıq olan kodlar (unikal indeks) atlanır.</summary>
    Task<CouponCodeImport> AddCodesAsync(IReadOnlyCollection<CouponCode> codes, CancellationToken cancellationToken);

    /// <summary>Kuponun kod sayları: cəmi, istifadəçilərə verilən, istifadə olunan.</summary>
    Task<CouponCodeCounts> CountsAsync(CouponId couponId, CancellationToken cancellationToken);

    /// <summary>İstifadəçiyə bu kuponlarda verilmiş kodlar.</summary>
    Task<IReadOnlyList<CouponCode>> ListAssignedAsync(Guid userId, IReadOnlyCollection<CouponId> couponIds, CancellationToken cancellationToken);

    /// <summary>Kuponda hələ heç kimə verilməmiş kod varmı.</summary>
    Task<bool> HasFreeAsync(CouponId couponId, CancellationToken cancellationToken);

    /// <summary>
    /// Boş kodlardan birini tək şərtli atomik yeniləmə ilə istifadəçiyə verir (<paramref name="slot"/> = istifadəçinin bu kupondakı neçənci kodu).
    /// Eyni yer paralel sorğuda artıq tutulubsa (unikal indeks) <see cref="CouponCodeAssignment.SlotTaken"/>, boş kod qalmayıbsa
    /// <see cref="CouponCodeAssignment.SoldOut"/>.
    /// </summary>
    Task<(CouponCodeAssignment Outcome, CouponCode? Code)> TryAssignAsync(
        CouponId couponId, Guid userId, int slot, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Bu kuponlardan birinin kodu (brend yoxlaması üçün).</summary>
    Task<CouponCode?> FindByCodeAsync(IReadOnlyCollection<CouponId> couponIds, string code, CancellationToken cancellationToken);

    /// <summary>Kodu yalnız hələ istifadə olunmayıbsa istifadə olunmuş kimi işarələyir (tək şərtli atomik yeniləmə).</summary>
    Task<bool> TryMarkUsedAsync(CouponCodeId id, Guid usedBy, DateTimeOffset now, CancellationToken cancellationToken);
}

public enum CouponCodeAssignment
{
    Assigned,
    SoldOut,
    SlotTaken,
}

public sealed record CouponCodeImport(int Added, int Duplicates);

public sealed record CouponCodeCounts(int Total, int Assigned, int Used);
