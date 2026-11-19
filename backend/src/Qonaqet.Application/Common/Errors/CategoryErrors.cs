using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Common.Errors;

public static class CategoryErrors
{
    public static readonly Error NotFound = Error.NotFound("category_not_found", "Kateqoriya tapılmadı.");

    public static readonly Error CodeTaken = Error.Conflict("category_code_taken", "Bu kodla kateqoriya artıq var.");

    public static readonly Error CodeInUse =
        Error.Conflict("category_code_in_use", "Kateqoriya artıq istifadədədir, onun kodu dəyişdirilə bilməz.");

    /// <summary>Silinə bilməz: səbəblər (aktiv təklif, kampaniya, hovuz balansı, etibarlı təsdiq) mesajda sabit kodlarla verilir.</summary>
    public static Error InUse(IReadOnlyList<string> reasons) => Error.Conflict(
        "category_in_use",
        $"Kateqoriya istifadədədir ({string.Join(", ", reasons)}): onu silmək olmaz, yalnız deaktiv etmək olar.");

    public static readonly Error IconInvalid =
        Error.Validation("category_icon_invalid", "İkon faylı tapılmadı və ya loqo/ikon təyinatlı şəkil deyil.");
}
