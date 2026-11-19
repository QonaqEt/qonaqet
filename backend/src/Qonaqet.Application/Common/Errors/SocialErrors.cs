using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Common.Errors;

/// <summary>Sosial qonaqlığın (təkliflər, claim-lər) sabit xəta kodları.</summary>
public static class SocialErrors
{
    public static readonly Error OfferNotFound = Error.NotFound("offer_not_found", "Təklif tapılmadı.");

    public static readonly Error CategoryInactive = Error.Validation("category_inactive", "Kateqoriya aktiv deyil.");

    public static readonly Error CatalogItemInvalid =
        Error.Validation("offer_catalog_item_invalid", "Kataloq elementi tapılmadı və ya bu biznesə aid deyil.");

    public static readonly Error OfferNotEditable = Error.Conflict("offer_not_editable", "Onlayn alınmış təklif dəyişdirilə bilməz.");

    public static readonly Error DonorNotFound = Error.NotFound("donor_not_found", "Bu email ilə QONAQET hesabı yoxdur.");
}
