namespace Qonaqet.Application.Features.Gifts.Common;

/// <summary>Hədiyyə parametrləri (<c>Gifts</c> bölməsi).</summary>
public sealed class GiftOptions
{
    public const string SectionName = "Gifts";

    /// <summary><c>expiresAt = deliverAt + ValidityDays</c> (default 60).</summary>
    public int ValidityDays { get; set; } = Domain.Gifts.GiftRules.DefaultValidityDays;

    /// <summary>
    /// Göndərən hovuz seçməyəndə müddəti bitən hədiyyənin bağışlandığı kateqoriya (onun default hovuzu). Yalnız konfiqurasiya dəyəridir —
    /// məntiq kateqoriya kodu üzrə şaxələnmir. Default <c>appsettings.json</c>-dadır.
    /// </summary>
    public string ExpiryDonationCategoryCode { get; set; } = string.Empty;
}
