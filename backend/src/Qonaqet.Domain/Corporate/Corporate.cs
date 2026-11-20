using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Corporate;

public readonly record struct CorporateId(Guid Value) : IStronglyTypedId<CorporateId>
{
    public static CorporateId From(Guid value) => new(value);
}

/// <summary>Şirkətin profili (admin yaradır və dəyişir).</summary>
/// <param name="BrandColor">Brend rəngi (<c>#RRGGBB</c>) — şirkətin öz məlumatıdır, platformanın rəngi deyil.</param>
public sealed record CorporateDetails(string Name, string Voen, Guid? LogoFileId, string? BrandColor, string ContactEmail);

/// <summary>
/// Şirkət (<c>corporates</c>): işçilərə/müştərilərə toplu hədiyyə və bonus göndərir, CSR kampaniyaları maliyyələşdirir. Şirkət istifadəçiləri
/// (<c>CorporateUser</c> rolu) <c>users.corporateId</c> ilə bağlanır.
/// </summary>
public sealed class Corporate : AggregateRoot<CorporateId>
{
    public const int NameMaxLength = 120;

    private Corporate(CorporateId id, DateTimeOffset createdAt, Guid? createdBy)
        : base(id, createdAt, createdBy)
    {
    }

    private Corporate()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string Voen { get; private set; } = string.Empty;

    public Guid? LogoFileId { get; private set; }

    public string? BrandColor { get; private set; }

    public string ContactEmail { get; private set; } = string.Empty;

    public static Corporate Create(CorporateDetails details, DateTimeOffset at, Guid? by)
    {
        var corporate = new Corporate(StronglyTypedId.New<CorporateId>(), Guard.Utc(at), by);
        corporate.Apply(details);
        return corporate;
    }

    public void Update(CorporateDetails details, DateTimeOffset at, Guid? by)
    {
        Apply(details);
        MarkUpdated(at, by);
    }

    /// <summary><c>#RRGGBB</c> formatı.</summary>
    public static bool IsValidColor(string? color) =>
        color is null || (color.Length == 7 && color[0] == '#' && color[1..].All(Uri.IsHexDigit));

    /// <summary>VÖEN: 10 rəqəm.</summary>
    public static bool IsValidVoen(string? voen) => voen is { Length: 10 } && voen.All(char.IsAsciiDigit);

    private void Apply(CorporateDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (!IsValidColor(details.BrandColor) || !IsValidVoen(details.Voen))
        {
            throw new ArgumentException("Brend rəngi və ya VÖEN düzgün deyil.", nameof(details));
        }

        Name = Guard.NotNullOrWhiteSpace(details.Name).Trim();
        Voen = details.Voen;
        LogoFileId = details.LogoFileId;
        BrandColor = details.BrandColor?.ToUpperInvariant();
        ContactEmail = Guard.NotNullOrWhiteSpace(details.ContactEmail).Trim().ToLowerInvariant();
    }
}
