using Qonaqet.Application.Features.Businesses.Common;
using Qonaqet.Application.Features.Social.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Social.Queries.PreviewCode;

/// <summary>
/// <c>POST business/{businessId}/redeem/preview</c>: işçi kodu skan edir/yazır və nəyi təhvil verəcəyini görür (məhsul, status, məbləğ,
/// etiket). Heç nə dəyişmir. Kod body-dədir (URL-də yox) və loglanmır.
/// </summary>
public sealed record PreviewCodeQuery(BusinessId BusinessId, string Code) : IQuery<RedemptionPreviewDto>;

public sealed class PreviewCodeHandler(BusinessAccess access, RedemptionCodeResolver resolver, IClock clock)
    : IQueryHandler<PreviewCodeQuery, RedemptionPreviewDto>
{
    public async Task<Result<RedemptionPreviewDto>> Handle(PreviewCodeQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var grant = await access.RequireAsync(request.BusinessId, BusinessPermission.Operate, cancellationToken).ConfigureAwait(false);
        if (grant.IsFailure)
        {
            return grant.Error;
        }

        var claim = await resolver.ResolveAsync(request.BusinessId, request.Code, cancellationToken).ConfigureAwait(false);
        if (claim.IsFailure)
        {
            return claim.Error;
        }

        return await resolver.PreviewAsync(claim.Value, clock.UtcNow, cancellationToken).ConfigureAwait(false);
    }
}
