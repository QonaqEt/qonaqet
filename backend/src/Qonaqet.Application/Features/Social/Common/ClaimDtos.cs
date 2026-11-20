using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Social;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Social.Common;

/// <summary>
/// Alıcının öz claim-i ("Aldıqlarım"): biznes, məhsul, ianəçi mətni, status və müddət. <see cref="Codes"/> yalnız rezerv qüvvədə olanda
/// verilir (təhvil verilmiş/bitmiş claim-in kodu göstərilmir).
/// </summary>
public sealed record ClaimDto(
    ClaimId Id,
    Guid BusinessId,
    string BusinessName,
    string Title,
    ClaimStatus Status,
    string DonorDisplayText,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RedeemedAt,
    ClaimCodesDto? Codes);

/// <summary>Təhvil kodları: QR payload, klientin çəkdiyi modul matrisi və qısa kod (<c>XXXX-XXXX</c>).</summary>
public sealed record ClaimCodesDto(string QrPayload, QrMatrix Qr, string ShortCode);

/// <summary>Claim DTO-sunu kodları ilə qurur. Kodlar saxlanılmır — hər dəfə ID-dən server sirri ilə törədilir.</summary>
public sealed class ClaimPresenter(IRedemptionCodes codes, IQrCodeEncoder qr, IClock clock)
{
    public ClaimDto Present(Claim claim, string businessName)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ClaimCodesDto? claimCodes = null;
        if (claim.IsRedeemableAt(clock.UtcNow))
        {
            var payload = codes.QrPayload(RedemptionCodeKind.Claim, claim.Id.Value);
            var shortCode = codes.ShortCode(RedemptionCodeKind.Claim, claim.Id.Value);
            claimCodes = new ClaimCodesDto(payload, qr.Encode(payload), $"{shortCode[..4]}-{shortCode[4..]}");
        }

        return new ClaimDto(
            claim.Id, claim.BusinessId.Value, businessName, claim.Title, claim.Status, claim.DonorDisplayText, claim.CreatedAt, claim.ExpiresAt,
            claim.RedeemedAt, claimCodes);
    }

    /// <summary>Yeni claim-ə qısa kodun hash-ini bağlayır (yazılmazdan əvvəl).</summary>
    public void AttachCodes(Claim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        var shortCode = codes.ShortCode(RedemptionCodeKind.Claim, claim.Id.Value);
        claim.AttachShortCode(codes.ShortCodeHash(shortCode) ?? throw new InvalidOperationException("Qısa kod yaradıla bilmədi."));
    }
}

/// <summary>
/// Ləyaqətlilik: istifadəçi aktiv kateqoriyaya aiddir, əgər kateqoriya açıqdırsa (<c>isOpen</c>) və ya onun həmin kateqoriya üçün
/// qüvvədə olan təsdiqi varsa. Yalnız kateqoriya sənədinin sahələri işlənir — kod üzrə heç bir şaxələnmə yoxdur.
/// </summary>
public sealed class SocialEligibility(ICategoryRepository categories)
{
    /// <summary>İstifadəçinin bu anda aid olduğu aktiv kateqoriyalar.</summary>
    public async Task<IReadOnlyList<Category>> EligibleCategoriesAsync(User user, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var active = await categories.ListActiveAsync(cancellationToken).ConfigureAwait(false);
        return [.. active.Where(c => IsEligible(user, c, now))];
    }

    public static bool IsEligible(User user, Category category, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(category);
        return category.IsActive && (category.IsOpen || user.VerifiedCategoryFor(category.Id.Value)?.IsValidAt(now) == true);
    }
}
