using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Plus.Common;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Plus.Queries.GetBrandCoupons;

/// <summary><c>GET brand/coupons</c>: brendin öz kuponları və kod statistikası (cəmi / verilən / istifadə olunan) — kimə verildiyi yox, yalnız saylar.</summary>
[RequiresRole(RoleNames.BrandUser)]
public sealed record GetBrandCouponsQuery : IQuery<IReadOnlyList<CouponDto>>;

public sealed class GetBrandCouponsHandler(BrandAccess access, ICouponRepository coupons, ICouponCodeRepository codes)
    : IQueryHandler<GetBrandCouponsQuery, IReadOnlyList<CouponDto>>
{
    public async Task<Result<IReadOnlyList<CouponDto>>> Handle(GetBrandCouponsQuery request, CancellationToken cancellationToken)
    {
        var brand = await access.RequireAsync(cancellationToken).ConfigureAwait(false);
        if (brand.IsFailure)
        {
            return brand.Error;
        }

        var list = await coupons.ListAsync(brand.Value.Id, 500, cancellationToken).ConfigureAwait(false);
        var result = new List<CouponDto>(list.Count);
        foreach (var coupon in list)
        {
            result.Add(coupon.ToDto(await codes.CountsAsync(coupon.Id, cancellationToken).ConfigureAwait(false)));
        }

        return result;
    }
}
