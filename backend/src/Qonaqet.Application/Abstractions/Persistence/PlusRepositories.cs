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
}

/// <summary><c>couponCodes</c>: <c>Unique</c> kuponların kodları.</summary>
public interface ICouponCodeRepository : IRepository<CouponCode, CouponCodeId>
{
    /// <summary>Kodları əlavə edir; kuponda artıq olan kodlar (unikal indeks) atlanır.</summary>
    Task<CouponCodeImport> AddCodesAsync(IReadOnlyCollection<CouponCode> codes, CancellationToken cancellationToken);

    /// <summary>Kuponun kod sayları: cəmi, istifadəçilərə verilən, istifadə olunan.</summary>
    Task<CouponCodeCounts> CountsAsync(CouponId couponId, CancellationToken cancellationToken);
}

public sealed record CouponCodeImport(int Added, int Duplicates);

public sealed record CouponCodeCounts(int Total, int Assigned, int Used);
