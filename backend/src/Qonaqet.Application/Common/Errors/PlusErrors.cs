using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Common.Errors;

/// <summary>QONAQET Plus (brendlər, kuponlar, kodlar) sabit xəta kodları.</summary>
public static class PlusErrors
{
    public static readonly Error BrandNotFound = Error.NotFound("brand_not_found", "Brend tapılmadı.");

    public static readonly Error BrandInvalid = Error.Validation("brand_invalid", "Brendin adı, email-i və saytı (http/https) düzgün olmalıdır.");

    public static readonly Error UserNotFound = Error.NotFound("user_not_found", "Bu email ilə QONAQET hesabı yoxdur.");

    public static readonly Error CouponNotFound = Error.NotFound("coupon_not_found", "Kupon tapılmadı.");

    public static readonly Error CouponInvalid = Error.Validation(
        "coupon_invalid", "Kuponun adı, limiti (1–100), kateqoriyaları və etibarlılıq tarixləri düzgün olmalıdır.");

    public static readonly Error CouponCodeInvalid = Error.Validation(
        "coupon_code_invalid", "Kod 3–40 simvol olmalıdır: böyük latın hərfləri, rəqəmlər və “-”.");

    public static readonly Error CategoryNotFound = Error.Validation("category_not_found", "Seçilmiş kateqoriyalardan biri tapılmadı.");

    /// <summary>Kod siyahısı yalnız <c>Unique</c> kupona yüklənir.</summary>
    public static readonly Error CodesNotUnique = Error.Conflict("coupon_not_unique", "Bu kupon ortaq koddur — ayrıca kod yüklənmir.");

    public static readonly Error CouponExpired = Error.Conflict("coupon_expired", "Bu kuponun müddəti bitib və ya hələ başlamayıb.");

    public static readonly Error CouponNotEligible =
        Error.Forbidden("category_not_eligible", "Bu kupon sizin statusunuza uyğun deyil. Statusunuzu profildə təsdiqləyə bilərsiniz.");

    public static readonly Error LimitReached = Error.Conflict("limit_reached", "Bu kupondan ala biləcəyiniz kodların sayına çatdınız.");

    public static readonly Error SoldOut = Error.Conflict("coupon_sold_out", "Bu kuponun kodları bitib.");

    public static readonly Error TooManyCodes = Error.Validation("coupon_codes_too_many", "Bir dəfəyə 1–5000 kod yükləmək olar.");
}
