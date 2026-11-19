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

    /// <summary>Təklifin bu gün (və ya askıda) qalan vahidi yoxdur.</summary>
    public static readonly Error QuotaExhausted = Error.Conflict("quota_exhausted", "Bu təklif bitib. Sabah yenidən baxın.");

    public static readonly Error OfferUnavailable = Error.Conflict("offer_unavailable", "Bu təklif hazırda aktiv deyil.");

    public static readonly Error OutsideWindow = Error.Conflict("offer_outside_window", "Bu təklif günün bu saatında verilmir.");

    public static readonly Error CategoryNotEligible =
        Error.Forbidden("category_not_eligible", "Bu təklif sizin statusunuza uyğun deyil. Statusunuzu profildə təsdiqləyə bilərsiniz.");

    // Bütün limit xətaları eyni sabit kodu (limit_reached) daşıyır; mesaj səbəbi deyir.
    public static readonly Error OfferAlreadyClaimedToday =
        Error.Conflict("limit_reached", "Bu təklifdən bu gün artıq istifadə etmisiniz. Sabah yenidən gələ bilərsiniz.");

    public static readonly Error DailyLimitReached = Error.Conflict("limit_reached", "Bugünkü limitə çatdınız. Sabah yenidən baxın.");

    public static readonly Error MonthlyLimitReached = Error.Conflict("limit_reached", "Bu ayın limitinə çatdınız.");

    public static readonly Error ActiveClaimExists =
        Error.Conflict("limit_reached", "Sizin artıq aktiv rezerviniz var. Onu istifadə edin və ya ləğv edin.");

    public static readonly Error DonorNotFound = Error.NotFound("donor_not_found", "Bu email ilə QONAQET hesabı yoxdur.");
}
