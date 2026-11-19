using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Common.Errors;

public static class VerificationErrors
{
    public static readonly Error NotFound = Error.NotFound("verification_not_found", "Təsdiq sorğusu tapılmadı.");

    public static readonly Error NotRequired =
        Error.Conflict("category_verification_not_required", "Bu kateqoriya açıqdır: təsdiq tələb olunmur.");

    public static readonly Error Pending =
        Error.Conflict("verification_pending", "Bu kateqoriya üçün baxılmamış sorğunuz var. Cavabı gözləyin.");

    public static readonly Error AlreadyVerified =
        Error.Conflict("already_verified", "Statusunuz artıq təsdiqlənib. Yeniləmə müddətin bitməsinə 14 gün qalmış mümkündür.");

    public static readonly Error IdentityAlreadyUsed =
        Error.Conflict("identity_already_used", "Bu FIN ilə bu kateqoriyada artıq başqa hesab təsdiqlənib və ya sorğu göndərib.");

    public static readonly Error AlreadyReviewed = Error.Conflict("verification_already_reviewed", "Bu sorğuya artıq baxılıb.");

    public static readonly Error NotApproved = Error.Conflict("verification_not_approved", "Müddət yalnız təsdiqlənmiş sorğuda dəyişdirilir.");

    public static readonly Error UserNotFound = Error.NotFound("user_not_found", "Sorğunun sahibi olan hesab tapılmadı.");

    public static readonly Error TooManyFiles = Error.Validation("files_count_invalid", "Çox sayda fayl göndərilib.");
}
