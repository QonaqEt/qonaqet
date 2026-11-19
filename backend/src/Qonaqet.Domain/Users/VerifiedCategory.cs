using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Users;

/// <summary>
/// İstifadəçinin təsdiqlənmiş kateqoriyası (<c>users.verifiedCategories[]</c>). Kateqoriyaya yalnız ID ilə istinad olunur.
/// <see cref="ValidUntil"/> eksklüziv sonluqdur (bu andan etibarən etibarsızdır); <c>null</c> müddətsizdir.
/// </summary>
public sealed class VerifiedCategory
{
    private VerifiedCategory(Guid categoryId, string? subtype, DateTimeOffset? validUntil, Guid verificationId, DateTimeOffset verifiedAt)
    {
        CategoryId = categoryId;
        Subtype = subtype;
        ValidUntil = validUntil;
        VerificationId = verificationId;
        VerifiedAt = verifiedAt;
    }

    private VerifiedCategory()
    {
    }

    public Guid CategoryId { get; private set; }

    public string? Subtype { get; private set; }

    public DateTimeOffset? ValidUntil { get; private set; }

    /// <summary>Təsdiqi verən sorğu (<c>verificationRequests</c>).</summary>
    public Guid VerificationId { get; private set; }

    public DateTimeOffset VerifiedAt { get; private set; }

    /// <summary>Xatırlatmanın göndərildiyi <see cref="ValidUntil"/> dəyəri: eyni dövr üçün ikinci xatırlatma olmasın (E05-T07).</summary>
    public DateTimeOffset? ReminderSentFor { get; private set; }

    public bool IsValidAt(DateTimeOffset now) => ValidUntil is not { } until || now < until;

    internal static VerifiedCategory Create(Guid categoryId, string? subtype, DateTimeOffset? validUntil, Guid verificationId, DateTimeOffset at) =>
        new(Guard.NotEmpty(categoryId), subtype, validUntil, Guard.NotEmpty(verificationId), Guard.Utc(at));

    internal void ChangeValidUntil(DateTimeOffset? validUntil) => ValidUntil = validUntil;
}
