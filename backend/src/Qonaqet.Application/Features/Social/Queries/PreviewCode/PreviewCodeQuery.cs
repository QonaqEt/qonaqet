using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Businesses.Common;
using Qonaqet.Application.Features.Gifts.Common;
using Qonaqet.Application.Features.Social.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Gifts;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Social.Queries.PreviewCode;

/// <summary>
/// <c>POST business/{businessId}/redeem/preview</c>: işçi kodu skan edir/yazır və nəyi təhvil verəcəyini görür — claim-də məhsul, status, məbləğ,
/// etiket; hədiyyədə qalan sətirlər və ya qalan məbləğ. Heç nə dəyişmir. Kod body-dədir (URL-də yox) və loglanmır. Göndərən, alıcı və
/// mesaj heç vaxt göstərilmir.
/// </summary>
public sealed record PreviewCodeQuery(BusinessId BusinessId, string Code) : IQuery<RedemptionPreviewDto>
{
    public override string ToString() => nameof(PreviewCodeQuery);
}

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

        var resolved = await resolver.ResolveAnyAsync(request.BusinessId, request.Code, cancellationToken).ConfigureAwait(false);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        if (resolved.Value.Gift is { } gift)
        {
            if (gift.Scope == GiftScope.Network && !grant.Value.Business.AcceptsNetworkVouchers)
            {
                return GiftErrors.NetworkNotAccepted;
            }

            return GiftRedemptionService.Preview(gift, clock.UtcNow);
        }

        return await resolver.PreviewAsync(resolved.Value.Claim!, clock.UtcNow, cancellationToken).ConfigureAwait(false);
    }
}
