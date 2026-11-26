using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Plus;

public readonly record struct BrandId(Guid Value) : IStronglyTypedId<BrandId>
{
    public static BrandId From(Guid value) => new(value);
}

/// <summary>Brendin profili (admin yaradır və dəyişir).</summary>
public sealed record BrandDetails(string Name, Guid? LogoFileId, string? Website, string ContactEmail, bool IsActive);

/// <summary>
/// QONAQET Plus brend partnyoru (<c>brands</c>): kuponlar təklif edir. Brend istifadəçiləri (<c>BrandUser</c> rolu) <c>users.brandId</c> ilə
/// bağlanır və yalnız öz brendlərinin kodlarını yoxlayır. Deaktiv brendin kuponları istifadəçilərə göstərilmir.
/// </summary>
public sealed class Brand : AggregateRoot<BrandId>
{
    public const int NameMaxLength = 120;

    public const int WebsiteMaxLength = 300;

    private Brand(BrandId id, DateTimeOffset createdAt, Guid? createdBy)
        : base(id, createdAt, createdBy)
    {
    }

    private Brand()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public Guid? LogoFileId { get; private set; }

    public string? Website { get; private set; }

    public string ContactEmail { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public static Brand Create(BrandDetails details, DateTimeOffset at, Guid? by)
    {
        var brand = new Brand(StronglyTypedId.New<BrandId>(), Guard.Utc(at), by);
        brand.Apply(details);
        return brand;
    }

    public void Update(BrandDetails details, DateTimeOffset at, Guid? by)
    {
        Apply(details);
        MarkUpdated(at, by);
    }

    public static bool IsValid(BrandDetails? details) =>
        details is not null
        && !string.IsNullOrWhiteSpace(details.Name) && details.Name.Trim().Length <= NameMaxLength
        && !string.IsNullOrWhiteSpace(details.ContactEmail) && details.ContactEmail.Contains('@', StringComparison.Ordinal) && details.ContactEmail.Length <= 254
        && (details.Website is null || IsHttpUrl(details.Website));

    private static bool IsHttpUrl(string value) =>
        value.Length <= WebsiteMaxLength
        && Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private void Apply(BrandDetails details)
    {
        if (!IsValid(details))
        {
            throw new ArgumentException("Brendin adı, email-i və ya saytı düzgün deyil.", nameof(details));
        }

        Name = details.Name.Trim();
        LogoFileId = details.LogoFileId;
        Website = details.Website;
        ContactEmail = details.ContactEmail.Trim().ToLowerInvariant();
        IsActive = details.IsActive;
    }
}
