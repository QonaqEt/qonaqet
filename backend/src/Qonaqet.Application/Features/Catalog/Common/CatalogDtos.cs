using Qonaqet.Domain.Catalog;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Catalog.Common;

/// <summary>Kataloq elementi (biznes paneli və ictimai səhifə).</summary>
public sealed record CatalogItemDto(
    CatalogItemId Id, Guid BusinessId, string Title, string? Description, long PriceQepik, Guid? ImageFileId, string? Group, bool IsActive, bool IsGiftable);

public static class CatalogMapping
{
    public static CatalogItemDto ToDto(this CatalogItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new CatalogItemDto(item.Id, item.BusinessId.Value, item.Title, item.Description, item.PriceQepik, item.ImageFileId, item.Group, item.IsActive, item.IsGiftable);
    }
}

/// <summary>Yaratma və yeniləmənin ortaq sahələri.</summary>
public interface ICatalogItemInput
{
    string Title { get; }

    string? Description { get; }

    long PriceQepik { get; }

    Guid? ImageFileId { get; }

    string? Group { get; }

    bool IsActive { get; }

    bool IsGiftable { get; }
}

public static class CatalogItemInput
{
    public static CatalogItemDetails ToDetails(this ICatalogItemInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new CatalogItemDetails(input.Title, input.Description, input.PriceQepik, input.ImageFileId, input.Group, input.IsActive, input.IsGiftable);
    }
}

/// <summary>Kataloq sahələrinin qaydaları. Qiymət müştəridən gəlsə də serverdə sərhədləri yoxlanılır.</summary>
public abstract class CatalogItemValidator<T> : Validator<T>
    where T : ICatalogItemInput
{
    protected CatalogItemValidator()
    {
        Rule("title", x => !string.IsNullOrWhiteSpace(x.Title) && x.Title.Length <= CatalogItem.TitleMaxLength, "title_invalid", $"Başlıq boş ola bilməz, ən çox {CatalogItem.TitleMaxLength} simvol.");
        Rule("description", x => x.Description is null || x.Description.Length <= CatalogItem.DescriptionMaxLength, ValidationCodes.TooLong, $"Təsvir ən çox {CatalogItem.DescriptionMaxLength} simvol ola bilər.");
        Rule("priceQepik", x => CatalogItem.IsValidPrice(x.PriceQepik), "price_invalid", "Qiymət 1 qəpik ilə 10 000 AZN arasında olmalıdır.");
        Rule("group", x => x.Group is null || x.Group.Length <= CatalogItem.GroupMaxLength, ValidationCodes.TooLong, $"Qrup adı ən çox {CatalogItem.GroupMaxLength} simvol ola bilər.");
        Rule("imageFileId", x => x.ImageFileId != Guid.Empty, "catalog_image_invalid", "Şəkil faylı düzgün deyil.");
    }
}
