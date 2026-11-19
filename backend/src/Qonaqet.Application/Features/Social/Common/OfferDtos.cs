using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Catalog;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Social;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Social.Common;

/// <summary>
/// Biznes panelində təklif. Kateqoriya yalnız ID və başqa tərəflərə görünən etiketlə verilir (həssas kateqoriyada "Qonaq");
/// alıcılar haqqında heç nə yoxdur — yalnız saylar.
/// </summary>
public sealed record OfferDto(
    OfferId Id,
    Guid BusinessId,
    OfferType Type,
    string Title,
    Guid CategoryId,
    string CategoryLabel,
    Guid? CatalogItemId,
    DonorDisplay DonorDisplay,
    string? DonorName,
    bool IsActive,
    int DailyQuota,
    int AvailableToday,
    string? AvailableFrom,
    string? AvailableTo,
    int TotalQuantity,
    int Remaining,
    DateTimeOffset CreatedAt);

/// <summary>İctimai təklif (tətbiqin "İsmarla" siyahısı): biznes, məhsul, qalan say və ianəçinin göstərilən adı.</summary>
public sealed record PublicOfferDto(
    OfferId Id,
    Guid BusinessId,
    string BusinessName,
    OfferType Type,
    string Title,
    Guid CategoryId,
    Guid? CatalogItemId,
    string DonorText,
    int Available,
    string? AvailableFrom,
    string? AvailableTo);

public static class OfferMapping
{
    public static OfferDto ToDto(this Offer offer, Category? category, DateOnly bakuToday)
    {
        ArgumentNullException.ThrowIfNull(offer);
        return new OfferDto(
            offer.Id, offer.BusinessId.Value, offer.Type, offer.Title, offer.CategoryId.Value, CategoryLabel.ForOtherParties(category),
            offer.CatalogItemId?.Value, offer.DonorDisplay, offer.DonorName, offer.IsActive, offer.DailyQuota, offer.AvailableToday(bakuToday),
            offer.AvailableFrom, offer.AvailableTo, offer.TotalQuantity, offer.Remaining, offer.CreatedAt);
    }

    public static PublicOfferDto ToPublicDto(this Offer offer, Business business, DateOnly bakuToday)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(business);
        return new PublicOfferDto(
            offer.Id, business.Id.Value, business.Name, offer.Type, offer.Title, offer.CategoryId.Value, offer.CatalogItemId?.Value,
            DonorTexts.Offer(offer, business), offer.AvailableToday(bakuToday), offer.AvailableFrom, offer.AvailableTo);
    }
}

/// <summary>Alıcıya göstərilən ianəçi mətnləri. Anonim ianəçinin adı heç vaxt ötürülmür.</summary>
public static class DonorTexts
{
    public const string Anonymous = "Bir xeyirxah insan";

    /// <summary>Göstərilən ad: adlı ianəçi → ad (gündəlik təklifdə ad yoxdursa biznesin adı); əks halda anonim.</summary>
    public static string Name(Offer offer, Business business)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(business);
        if (offer.DonorDisplay != DonorDisplay.Named)
        {
            return Anonymous;
        }

        return offer.DonorName ?? (offer.Type == OfferType.BusinessDaily ? business.Name : Anonymous);
    }

    /// <summary>Siyahıda: "Leyla ısmarlayır".</summary>
    public static string Offer(Offer offer, Business business) => $"{Name(offer, business)} ısmarlayır";

    /// <summary>Claim-də alıcıya: "Leyla sizə ısmarladı 💚".</summary>
    public static string Claim(Offer offer, Business business) => $"{Name(offer, business)} sizə ısmarladı 💚";
}

/// <summary>Təklif yaradılıb/dəyişdiriləndə ortaq yoxlamalar: kateqoriya aktivdir, kataloq elementi bu biznesindir.</summary>
public sealed class OfferReferenceCheck(ICategoryRepository categories, ICatalogItemRepository catalog)
{
    public async Task<Result<Category>> CategoryAsync(CategoryId categoryId, CancellationToken cancellationToken)
    {
        var category = await categories.GetAsync(categoryId, cancellationToken).ConfigureAwait(false);
        if (category is null)
        {
            return CategoryErrors.NotFound;
        }

        if (!category.IsActive)
        {
            return SocialErrors.CategoryInactive;
        }

        return category;
    }

    public async Task<bool> CatalogItemUsableAsync(BusinessId businessId, CatalogItemId? itemId, CancellationToken cancellationToken)
    {
        if (itemId is not { } id)
        {
            return true;
        }

        var item = await catalog.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return item is not null && item.BusinessId == businessId;
    }
}
