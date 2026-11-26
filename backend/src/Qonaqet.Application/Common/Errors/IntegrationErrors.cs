using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Common.Errors;

/// <summary>Partner API və inteqrasiya client-lərinin sabit xəta kodları (OAuth2 kodları RFC 6749-dakı kimi).</summary>
public static class IntegrationErrors
{
    public static readonly Error InvalidClient = Error.Unauthorized("invalid_client", "client_id və ya client_secret yanlışdır.");

    public static readonly Error InvalidScope = Error.Validation("invalid_scope", "İstənən scope bu client-ə verilməyib.");

    public static readonly Error UnsupportedGrantType = Error.Validation("unsupported_grant_type", "Yalnız grant_type=client_credentials dəstəklənir.");

    public static readonly Error ClientNotFound = Error.NotFound("integration_client_not_found", "Client tapılmadı.");

    public static readonly Error ClientInvalid = Error.Validation(
        "integration_client_invalid", "Ad, scope-lar (məlum siyahıdan), bizneslər (≤ 200) və rate limit (10–6000/dəq) düzgün olmalıdır.");

    /// <summary>Biznes client-in <c>business_ids</c>-ində deyil (mövcudluğu açıqlanmır).</summary>
    public static readonly Error BusinessNotAllowed = Error.Forbidden("business_not_allowed", "Bu client bu biznes adından işləyə bilməz.");

    /// <summary>Təhvil tapılmadı və ya bu client tərəfindən edilməyib.</summary>
    public static readonly Error RedemptionNotFound = Error.NotFound("redemption_not_found", "Təhvil tapılmadı.");

    public static readonly Error ReverseWindowPassed = Error.Conflict("reverse_window_passed", "Təhvil yalnız 15 dəqiqə ərzində geri alına bilər.");

    public static readonly Error AlreadyReversed = Error.Conflict("already_reversed", "Bu təhvil artıq geri alınıb.");

    /// <summary>Claim geri qayıda bilmir: alıcının artıq başqa aktiv rezervi var.</summary>
    public static readonly Error ReverseConflict = Error.Conflict("reverse_conflict", "Təhvil geri alına bilmədi: claim-in sahibinin başqa aktiv rezervi var.");

    /// <summary>Token-in client-i ləğv olunub.</summary>
    public static readonly Error ClientRevoked = Error.Unauthorized("invalid_client", "Bu client ləğv olunub.");
}
