using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Features.Integration.Common;
using Qonaqet.Application.Features.Social.Common;
using Qonaqet.Domain.Integration;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Integration.Queries.PreviewPartnerCode;

/// <summary>
/// <c>GET /partner/v1/codes/{code}?businessId=</c> (<c>codes:read</c>): hədiyyə, claim və ya şəbəkə vauçerinin önizlənməsi — növ, qalıq
/// (sətirlər / məbləğ), müddət və bu biznesdə təhvil oluna bilərmi (<c>canRedeem</c>; şəbəkə vauçerini qəbul etməyən biznesdə
/// <c>network_voucher_not_accepted</c>). İşçi önizləməsi ilə eyni məlumatdır: alıcı, göndərən, ianəçi və mesaj heç vaxt qaytarılmır.
/// </summary>
[RequiresRole(PartnerScopes.CodesRead)]
public sealed record PreviewPartnerCodeQuery(Guid BusinessId, string Code) : IQuery<RedemptionPreviewDto>
{
    /// <summary>Kod bearer-dir: loga düşməsin.</summary>
    public override string ToString() => nameof(PreviewPartnerCodeQuery);
}

public sealed class PreviewPartnerCodeHandler(PartnerAccess access, CodeRedemption redemption) : IQueryHandler<PreviewPartnerCodeQuery, RedemptionPreviewDto>
{
    public async Task<Result<RedemptionPreviewDto>> Handle(PreviewPartnerCodeQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var granted = await access.RequireBusinessAsync(request.BusinessId, cancellationToken).ConfigureAwait(false);
        return granted.IsFailure
            ? granted.Error
            : await redemption.PreviewAsync(granted.Value.Business, request.Code, cancellationToken).ConfigureAwait(false);
    }
}
