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

    /// <summary>Aktor bu biznesin üzvü deyil (və ya lazımi rolu yoxdur). Biznesin mövcudluğu üzv olmayana açıqlanmır.</summary>
    public static readonly Error AccessDenied = Error.Forbidden("business_access_denied", "Bu biznesdə bu əməliyyat üçün icazəniz yoxdur.");

    public static readonly Error StaffAccountNotFound =
        Error.NotFound("staff_account_not_found", "Bu email ilə QONAQET hesabı yoxdur. İşçi əvvəlcə tətbiqdə qeydiyyatdan keçməlidir.");

    public static readonly Error AlreadyMember = Error.Conflict("staff_already_member", "Bu istifadəçi artıq biznesin üzvüdür.");

    public static readonly Error MemberNotFound = Error.NotFound("staff_member_not_found", "Belə işçi tapılmadı.");

    public static readonly Error CannotRemoveOwner = Error.Conflict("cannot_remove_owner", "Biznes sahibini işçi kimi çıxarmaq olmaz.");

    public static readonly Error NotActive = Error.Conflict("business_not_active", "Biznes aktiv deyil.");
}
