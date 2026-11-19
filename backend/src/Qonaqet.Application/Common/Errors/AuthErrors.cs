using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Common.Errors;

/// <summary>Auth və hesab xəta kodları (sabit API müqaviləsi).</summary>
public static class AuthErrors
{
    public static readonly Error OtpSendLimit =
        Error.TooManyRequests("otp_send_limit", "Çox sayda kod istənilib. Bir az sonra yenidən cəhd edin.");

    public static readonly Error OtpInvalid =
        Error.Validation("otp_invalid", "Kod yanlışdır və ya vaxtı bitib.");

    public static readonly Error UserNotFound =
        Error.NotFound("user_not_found", "İstifadəçi tapılmadı.");

    public static readonly Error FirstNameRequired =
        Error.Validation("first_name_required", "Ad boş ola bilməz.");

    public static readonly Error CannotRemoveOwnAdmin =
        Error.Conflict("cannot_remove_own_admin", "Öz admin rolunuzu silə bilməzsiniz.");

    public static readonly Error RefreshTokenInvalid =
        Error.Unauthorized("refresh_token_invalid", "Sessiya etibarsızdır. Yenidən daxil olun.");

    public static readonly Error RefreshTokenReused =
        Error.Unauthorized("refresh_token_reused", "Sessiya təhlükəsizlik səbəbilə bağlandı. Yenidən daxil olun.");
}
