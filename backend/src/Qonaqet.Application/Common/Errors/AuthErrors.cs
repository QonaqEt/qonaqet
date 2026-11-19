using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Common.Errors;

/// <summary>Auth və hesab xəta kodları (sabit API müqaviləsi).</summary>
public static class AuthErrors
{
    public static readonly Error OtpSendLimit =
        Error.TooManyRequests("otp_send_limit", "Çox sayda kod istənilib. Bir az sonra yenidən cəhd edin.");

    public static readonly Error OtpInvalid =
        Error.Validation("otp_invalid", "Kod yanlışdır və ya vaxtı bitib.");
}
