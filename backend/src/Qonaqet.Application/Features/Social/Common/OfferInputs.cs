using Qonaqet.Domain.Catalog;
using Qonaqet.Domain.Social;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Social.Common;

/// <summary>Gündəlik təklifin yaradılma və yenilənmə sahələri.</summary>
public interface IDailyOfferInput
{
    string Title { get; }

    CatalogItemId? CatalogItemId { get; }

    int DailyQuota { get; }

    string? AvailableFrom { get; }

    string? AvailableTo { get; }

    DonorDisplay DonorDisplay { get; }

    string? DonorName { get; }

    bool IsActive { get; }
}

public static class DailyOfferInput
{
    public static DailyOfferDetails ToDetails(this IDailyOfferInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new DailyOfferDetails(
            input.Title, input.CatalogItemId, input.DailyQuota, input.AvailableFrom, input.AvailableTo, input.DonorDisplay, input.DonorName, input.IsActive);
    }
}

/// <summary>Təklif sahələrinin ortaq qaydaları (başlıq, ianəçi adı).</summary>
public static class OfferRules
{
    public static bool IsValidTitle(string? title) => !string.IsNullOrWhiteSpace(title) && title.Trim().Length <= Offer.TitleMaxLength;

    public static bool IsValidDonorName(string? name) => name is null || name.Trim().Length <= Offer.DonorNameMaxLength;
}

public abstract class DailyOfferValidator<T> : Validator<T>
    where T : IDailyOfferInput
{
    protected DailyOfferValidator()
    {
        Rule("title", x => OfferRules.IsValidTitle(x.Title), "title_invalid", $"Başlıq boş ola bilməz, ən çox {Offer.TitleMaxLength} simvol.");
        Rule("dailyQuota", x => Offer.IsValidDailyQuota(x.DailyQuota), "daily_quota_invalid", $"Gündəlik kvota 1 ilə {Offer.MaxDailyQuota} arasında olmalıdır.");
        Rule("availableFrom", x => Offer.IsValidWindow(x.AvailableFrom, x.AvailableTo), "offer_window_invalid",
            "Vaxt pəncərəsi ya boş olmalıdır, ya da iki fərqli HH:mm vaxtı.");
        Rule("donorDisplay", x => Enum.IsDefined(x.DonorDisplay), "donor_display_invalid", "Göstərilmə növü Anonymous və ya Named olmalıdır.");
        Rule("donorName", x => OfferRules.IsValidDonorName(x.DonorName), ValidationCodes.TooLong, $"Ad ən çox {Offer.DonorNameMaxLength} simvol ola bilər.");
        Rule("catalogItemId", x => x.CatalogItemId is not { } id || id.Value != Guid.Empty, "offer_catalog_item_invalid", "Kataloq elementi düzgün deyil.");
    }
}
