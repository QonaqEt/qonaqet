using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Common.Errors;

/// <summary>Bütün sahələrə aid sabit xəta kodları. Kodlar API müqaviləsidir, dəyişdirilmir.</summary>
public static class CommonErrors
{
    public static readonly Error Unauthorized = Error.Unauthorized("unauthorized", "Daxil olmaq lazımdır.");

    public static readonly Error Forbidden = Error.Forbidden("forbidden", "Bu əməliyyat üçün icazəniz yoxdur.");

    public static readonly Error IdempotencyKeyRequired =
        Error.Validation("idempotency_key_required", "Bu əməliyyat üçün Idempotency-Key başlığı məcburidir.");

    public static readonly Error IdempotencyKeyReused =
        Error.Conflict("idempotency_key_reused", "Bu Idempotency-Key fərqli sorğu ilə artıq işlədilib.");
}
