using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Social;
using Qonaqet.Domain.Users;

namespace Qonaqet.Application.Features.Social.Common;

/// <summary>Alıcının öz claim-i ("Aldıqlarım"): biznes, məhsul, ianəçi mətni, status və müddət.</summary>
public sealed record ClaimDto(
    ClaimId Id,
    Guid BusinessId,
    string BusinessName,
    string Title,
    ClaimStatus Status,
    string DonorDisplayText,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RedeemedAt);

public static class ClaimMapping
{
    public static ClaimDto ToDto(this Claim claim, string businessName)
    {
        ArgumentNullException.ThrowIfNull(claim);
        return new ClaimDto(
            claim.Id, claim.BusinessId.Value, businessName, claim.Title, claim.Status, claim.DonorDisplayText, claim.CreatedAt, claim.ExpiresAt,
            claim.RedeemedAt);
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
