namespace Qonaqet.Application.Abstractions.Services;

/// <summary>Təhvil kodunun növü: QR payload-un prefiksi (<c>c</c> = claim, <c>g</c> = hədiyyə, E09).</summary>
public enum RedemptionCodeKind
{
    Claim,
    Gift,
}

/// <summary>Kodun açılmış forması: növ və obyektin ID-si (HMAC yoxlanılıb).</summary>
public sealed record RedemptionCodeRef(RedemptionCodeKind Kind, Guid Id);

/// <summary>
/// Təhvil kodları. QR payload: <c>base64url(prefix + "." + id + "." + HMAC)</c> — imza ayrıca sirr açarı ilə (<c>Security:QrSigningSecret</c>),
/// ona görə saxta kod yaratmaq olmur. Qısa kod: eyni açarla ID-dən törədilən 8 simvollu Crockford base32 (~40 bit, təxmin edilə bilməz,
/// endpoint-lər rate-limitlidir); DB-də yalnız onun hash-i saxlanılır. Kodlar bearer-dir: heç vaxt loglanmır.
/// </summary>
public interface IRedemptionCodes
{
    string QrPayload(RedemptionCodeKind kind, Guid id);

    /// <summary>QR payload-u açır və imzanı sabit vaxtda yoxlayır; saxta/pozulmuş kod <c>null</c>.</summary>
    RedemptionCodeRef? ParseQr(string? payload);

    /// <summary>İnsan üçün oxunaqlı qısa kod (<c>XXXX-XXXX</c> göstərilir).</summary>
    string ShortCode(RedemptionCodeKind kind, Guid id);

    /// <summary>Daxil edilmiş qısa kodun normal formasının hash-i (boşluq, defis, O/0, I/L/1 qarışıqlığı düzəldilir); düzgün deyilsə <c>null</c>.</summary>
    string? ShortCodeHash(string? input);
}
