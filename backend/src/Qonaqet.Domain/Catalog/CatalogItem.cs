using Qonaqet.Domain.Businesses;
using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Catalog;

public readonly record struct CatalogItemId(Guid Value) : IStronglyTypedId<CatalogItemId>
{
    public static CatalogItemId From(Guid value) => new(value);
}

/// <summary>
/// Biznesin kataloq elementi (<c>catalogItems</c>). Ümumi anlayışdır: içki, yemək, gül, kitab, xidmət ola bilər.
/// Qiymət <c>long</c> qəpikdir və yalnız serverdə yoxlanılır. Silmə həmişə soft delete-dir; hədiyyələr
/// elementi qiymət/başlıq snapshot-ı ilə saxlayır, ona görə sonrakı dəyişiklik köhnə hədiyyələrə təsir etmir.
/// </summary>
public sealed class CatalogItem : AggregateRoot<CatalogItemId>
{
    public const int TitleMaxLength = 120;
    public const int DescriptionMaxLength = 1000;
    public const int GroupMaxLength = 60;
    public const int ExternalIdMaxLength = 100;

    /// <summary>1 qəpik – 10 000 AZN.</summary>
    public const long MinPriceQepik = 1;
    public const long MaxPriceQepik = 1_000_000;

    private CatalogItem(CatalogItemId id, BusinessId businessId, CatalogItemDetails details, DateTimeOffset createdAt, Guid? createdBy)
        : base(id, createdAt, createdBy)
    {
        BusinessId = businessId;
        Apply(details);
    }

    private CatalogItem()
    {
    }

    public BusinessId BusinessId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public long PriceQepik { get; private set; }

    public Guid? ImageFileId { get; private set; }

    /// <summary>Menyu qrupu, məs. "İsti içkilər".</summary>
    public string? Group { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>Hədiyyə kimi seçilə bilər (<c>Items</c> növlü hədiyyələr yalnız belə elementlərdən).</summary>
    public bool IsGiftable { get; private set; }

    /// <summary>Partnyor sisteminin (POS) öz ID-si — kataloq sinxronunda upsert açarı; biznes daxilində unikal. Əl ilə yaradılan məhsulda yoxdur.</summary>
    public string? ExternalId { get; private set; }

    public static bool IsValidPrice(long priceQepik) => priceQepik is >= MinPriceQepik and <= MaxPriceQepik;

    public static CatalogItem Create(BusinessId businessId, CatalogItemDetails details, DateTimeOffset at, Guid? by, string? externalId = null)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (externalId is not null && !IsValidExternalId(externalId))
        {
            throw new ArgumentException("externalId düzgün deyil.", nameof(externalId));
        }

        return new CatalogItem(StronglyTypedId.New<CatalogItemId>(), businessId, details, at, by) { ExternalId = externalId };
    }

    public static bool IsValidExternalId(string? externalId) =>
        !string.IsNullOrWhiteSpace(externalId) && externalId.Length <= ExternalIdMaxLength && externalId == externalId.Trim();

    /// <summary>Sahələr fərqlidirmi (sinxronda dəyişməyən məhsul yazılmır).</summary>
    public bool Differs(CatalogItemDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        return Title != details.Title.Trim()
            || Description != (string.IsNullOrWhiteSpace(details.Description) ? null : details.Description.Trim())
            || PriceQepik != details.PriceQepik
            || ImageFileId != details.ImageFileId
            || Group != (string.IsNullOrWhiteSpace(details.Group) ? null : details.Group.Trim())
            || IsActive != details.IsActive
            || IsGiftable != details.IsGiftable;
    }

    public void Update(CatalogItemDetails details, DateTimeOffset at, Guid? by)
    {
        ArgumentNullException.ThrowIfNull(details);
        Apply(details);
        MarkUpdated(at, by);
    }

    private void Apply(CatalogItemDetails details)
    {
        Title = Guard.NotNullOrWhiteSpace(details.Title).Trim();
        Description = string.IsNullOrWhiteSpace(details.Description) ? null : details.Description.Trim();
        PriceQepik = IsValidPrice(details.PriceQepik) ? details.PriceQepik : throw new ArgumentOutOfRangeException(nameof(details), "Qiymət düzgün deyil.");
        ImageFileId = details.ImageFileId;
        Group = string.IsNullOrWhiteSpace(details.Group) ? null : details.Group.Trim();
        IsActive = details.IsActive;
        IsGiftable = details.IsGiftable;
    }
}

public sealed record CatalogItemDetails(
    string Title, string? Description, long PriceQepik, Guid? ImageFileId, string? Group, bool IsActive, bool IsGiftable);
