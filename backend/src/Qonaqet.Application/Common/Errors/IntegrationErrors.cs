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

    public static readonly Error InsufficientPartnerBalance =
        Error.Conflict("insufficient_partner_balance", "Partnyor balansı bu hədiyyə üçün kifayət etmir. Balansı artırın.");

    public static readonly Error TopUpInvalid = Error.Validation("top_up_invalid", "Balans artımı 10 ilə 100 000 AZN arasında olmalıdır.");

    public static readonly Error WebhookUrlInvalid = Error.Validation("webhook_url_invalid", "Webhook URL-i HTTPS olmalıdır (≤ 500 simvol).");

    public static readonly Error WebhookLimitReached = Error.Conflict("webhook_limit_reached", "Client başına ən çox 10 webhook abunəsi ola bilər.");

    public static readonly Error WebhookNotFound = Error.NotFound("webhook_not_found", "Webhook abunəsi tapılmadı.");

    public static readonly Error WebhookDeliveryNotFound = Error.NotFound("webhook_delivery_not_found", "Çatdırılma tapılmadı.");

    public static readonly Error WebhookDeliveryBusy = Error.Conflict("webhook_delivery_busy", "Çatdırılma elə indi göndərilir; bir az sonra yenidən cəhd edin.");

    /// <summary>Sandbox və real dünya qarışmır (E13-T09): sandbox client/hədiyyə yalnız sandbox biznesdə, real olanlar yalnız real biznesdə.</summary>
    public static readonly Error SandboxMismatch =
        Error.Validation("sandbox_mismatch", "Sandbox (test) client-i, hədiyyəsi və biznesi yalnız bir-biri ilə işləyir; real biznesdə istifadə olunmur.");

    public static readonly Error SandboxOnly = Error.Forbidden("sandbox_only", "Bu əməliyyat yalnız sandbox client-ləri üçündür.");

    /// <summary>Token-in client-i ləğv olunub.</summary>
    public static readonly Error ClientRevoked = Error.Unauthorized("invalid_client", "Bu client ləğv olunub.");
}
