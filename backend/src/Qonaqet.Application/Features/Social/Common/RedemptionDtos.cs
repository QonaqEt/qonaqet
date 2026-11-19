using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Social;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Social.Common;

/// <summary>Təhvil olunan şeyin növü (hədiyyələr E09-da əlavə olunur).</summary>
public enum RedemptionKind
{
    Claim,
}

/// <summary>
/// İşçinin gördüyü yeganə məlumat: nə (məhsul), status, məbləğ (pullu mənbədə) və neytral etiket. Alıcının adı, email-i, ID-si,
/// ianəçi və həssas kateqoriyanın adı heç vaxt burada olmur.
/// </summary>
public sealed record RedemptionPreviewDto(
    RedemptionKind Kind,
    Guid Id,
    string Title,
    ClaimStatus Status,
    long? AmountQepik,
    string Label,
    DateTimeOffset ExpiresAt,
    bool CanRedeem,
    DateTimeOffset? RedeemedAt);

/// <summary>
/// Daxil edilmiş kodu (QR payload və ya qısa kod) bu biznesin claim-inə çevirir. Başqa biznesin claim-i, saxta imza və tanınmayan kod
/// eyni <c>code_not_found</c> alır — kodun başqa yerdə mövcudluğu açıqlanmır.
/// </summary>
public sealed class RedemptionCodeResolver(IRedemptionCodes codes, IClaimRepository claims, ICategoryRepository categories)
{
    public async Task<Result<Claim>> ResolveAsync(BusinessId businessId, string? code, CancellationToken cancellationToken)
    {
        Claim? claim = null;
        if (codes.ParseQr(code) is { } parsed)
        {
            if (parsed.Kind == RedemptionCodeKind.Claim)
            {
                claim = await claims.GetAsync(ClaimId.From(parsed.Id), cancellationToken).ConfigureAwait(false);
            }
        }
        else if (codes.ShortCodeHash(code) is { } hash)
        {
            claim = await claims.FindByShortCodeAsync(businessId, hash, cancellationToken).ConfigureAwait(false);
        }

        if (claim is null || claim.BusinessId != businessId)
        {
            return SocialErrors.CodeNotFound;
        }

        return claim;
    }

    public async Task<RedemptionPreviewDto> PreviewAsync(Claim claim, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        var category = await categories.GetAsync(claim.CategoryId, cancellationToken).ConfigureAwait(false);
        return new RedemptionPreviewDto(
            RedemptionKind.Claim, claim.Id.Value, claim.Title, EffectiveStatus(claim, now), claim.AmountQepik, CategoryLabel.ForOtherParties(category),
            claim.ExpiresAt, claim.IsRedeemableAt(now), claim.RedeemedAt);
    }

    /// <summary>Müddəti keçmiş, amma job-un hələ bağlamadığı rezerv işçiyə <c>Expired</c> görünür.</summary>
    public static ClaimStatus EffectiveStatus(Claim claim, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(claim);
        return claim.Status == ClaimStatus.Reserved && !claim.IsRedeemableAt(now) ? ClaimStatus.Expired : claim.Status;
    }

    /// <summary>Təhvil verilə bilməyən claim-in sabit xətası.</summary>
    public static Error NotRedeemable(Claim claim, DateTimeOffset now) => EffectiveStatus(claim, now) switch
    {
        ClaimStatus.Redeemed => SocialErrors.AlreadyRedeemed,
        ClaimStatus.Expired => SocialErrors.ClaimExpired,
        _ => SocialErrors.ClaimCancelled,
    };
}
