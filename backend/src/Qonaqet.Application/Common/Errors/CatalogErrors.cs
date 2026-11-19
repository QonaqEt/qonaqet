using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Common.Errors;

public static class CatalogErrors
{
    public static readonly Error NotFound = Error.NotFound("catalog_item_not_found", "Kataloq elementi tapılmadı.");

    public static readonly Error ImageInvalid =
        Error.Validation("catalog_image_invalid", "Şəkil faylı tapılmadı, sizə aid deyil və ya kataloq şəkli deyil.");
}
