using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Social;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Social.Common;

/// <summary>Təhvil olunan şeyin növü.</summary>
public enum RedemptionKind
{
    Claim,
    Gift,
}

/// <summary>
/// İşçinin gördüyü yeganə məlumat: nə (məhsul), status, məbləğ (pullu mənbədə) və neytral etiket. Alıcının adı, email-i, ID-si,
/// ianəçi, göndərən, mesaj və həssas kateqoriyanın adı heç vaxt burada olmur. Hədiyyədə <see cref="Gift"/> qalan sətirləri/məbləği verir ki,
/// işçi nəyi təhvil verəcəyini seçsin.
/// </summary>
/// <param name="Status">Claim-də <see cref="ClaimStatus"/>, hədiyyədə <see cref="GiftStatus"/> (mətn kimi).</param>
public sealed record RedemptionPreviewDto(
    RedemptionKind Kind,
    Guid Id,
    string Title,
    string Status,
    long? AmountQepik,
    string Label,
    DateTimeOffset ExpiresAt,
    bool CanRedeem,
    DateTimeOffset? RedeemedAt,
    GiftRedemptionDetailsDto? Gift = null);

/// <summary>Hədiyyənin təhvil üçün görünüşü: növ, qalan sətirlər (məhsulda) və ya qalan məbləğ (vauçerdə), indicə təhvil verilən dəyər.</summary>
public sealed record GiftRedemptionDetailsDto(
    GiftKind Kind, GiftScope Scope, IReadOnlyList<GiftRedeemableItemDto> Items, long RemainingQepik, long? RedeemedNowQepik);

public sealed record GiftRedeemableItemDto(Guid CatalogItemId, string Title, long UnitPriceQepik, int RemainingQty);

/// <summary>Kodun hədəfi: claim və ya hədiyyə (biri dolu olur).</summary>
public sealed record ResolvedCode(Claim? Claim, Gift? Gift);

/// <summary>
/// Daxil edilmiş kodu (QR payload və ya qısa kod) bu biznesin claim-inə çevirir. Başqa biznesin claim-i, saxta imza və tanınmayan kod
/// eyni <c>code_not_found</c> alır — kodun başqa yerdə mövcudluğu açıqlanmır.
/// </summary>
public sealed class RedemptionCodeResolver(IRedemptionCodes codes, IClaimRepository claims, IGiftRepository gifts, ICategoryRepository categories)
{
    /// <summary>
    /// Kodun hədəfi. Claim yalnız öz biznesində; biznes hədiyyəsi yalnız öz biznesində; şəbəkə vauçeri istənilən biznesdə (qəbul edib-etməməsini
    /// təhvil yoxlayır). Ödənilməmiş/ləğv olunmuş hədiyyə tanınmır.
    /// </summary>
    public async Task<Result<ResolvedCode>> ResolveAnyAsync(BusinessId businessId, string? code, CancellationToken cancellationToken)
    {
        Claim? claim = null;
        Gift? gift = null;
        if (codes.ParseQr(code) is { } parsed)
        {
            if (parsed.Kind == RedemptionCodeKind.Claim)
            {
                claim = await claims.GetAsync(ClaimId.From(parsed.Id), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                gift = await gifts.GetAsync(GiftId.From(parsed.Id), cancellationToken).ConfigureAwait(false);
            }
        }
        else if (codes.ShortCodeHash(code) is { } hash)
        {
            claim = await claims.FindByShortCodeAsync(businessId, hash, cancellationToken).ConfigureAwait(false);
            if (claim is null)
            {
                gift = await gifts.FindByShortCodeAsync(hash, cancellationToken).ConfigureAwait(false);
            }
        }

        if (claim is not null)
        {
            return claim.BusinessId == businessId ? new ResolvedCode(claim, null) : SocialErrors.CodeNotFound;
        }

        if (gift is null || gift.Status is GiftStatus.AwaitingPayment or GiftStatus.Cancelled
            || (gift.Scope == GiftScope.Business && gift.BusinessId != businessId))
        {
            return SocialErrors.CodeNotFound;
        }

        return new ResolvedCode(null, gift);
    }

    public async Task<RedemptionPreviewDto> PreviewAsync(Claim claim, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        var category = await categories.GetAsync(claim.CategoryId, cancellationToken).ConfigureAwait(false);
        return new RedemptionPreviewDto(
            RedemptionKind.Claim, claim.Id.Value, claim.Title, EffectiveStatus(claim, now).ToString(), claim.AmountQepik, CategoryLabel.ForOtherParties(category),
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
