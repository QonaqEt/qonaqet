using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Common.Errors;

public static class BusinessErrors
{
    public static readonly Error NotFound = Error.NotFound("business_not_found", "Biznes tapılmadı.");

    public static readonly Error VoenTaken = Error.Conflict("voen_already_registered", "Bu VÖEN ilə biznes artıq qeydiyyatdadır.");

    public static readonly Error ApplicationPending =
        Error.Conflict("business_application_pending", "Baxılmamış müraciətiniz var. Cavabı gözləyin.");

    public static readonly Error SlugUnavailable = Error.Conflict("business_slug_unavailable", "Bu ad üçün ünvan yaratmaq mümkün olmadı. Adı dəqiqləşdirin.");

    public static readonly Error FileInvalid =
        Error.Validation("business_file_invalid", "Şəkil faylı tapılmadı, sizə aid deyil və ya növü uyğun deyil.");

    public static readonly Error OwnerNotFound = Error.NotFound("business_owner_not_found", "Müraciət edən istifadəçinin hesabı tapılmadı.");

    public static readonly Error NotActive = Error.Conflict("business_not_active", "Biznes aktiv deyil.");
}
