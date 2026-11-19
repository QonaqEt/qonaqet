namespace Qonaqet.Domain.Categories;

/// <summary>
/// Kateqoriyanın başqa tərəflərə (biznes işçisi, ianəçi, lövhə) görünən adı. Həssas kateqoriyanın adı heç vaxt göstərilmir:
/// əvəzinə neytral "Qonaq" yazılır, ona görə işçi qarşısındakı insanın statusunu bilmir.
/// </summary>
public static class CategoryLabel
{
    public const string Neutral = "Qonaq";

    public static string ForOtherParties(Category? category) =>
        category is { PrivacyLevel: CategoryPrivacyLevel.Public } ? category.Name : Neutral;
}
