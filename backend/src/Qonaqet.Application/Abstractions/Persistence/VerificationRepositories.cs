using Qonaqet.Application.Common;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Verification;

namespace Qonaqet.Application.Abstractions.Persistence;

/// <summary><c>verificationRequests</c>.</summary>
public interface IVerificationRequestRepository : IRepository<VerificationRequest, VerificationRequestId>
{
    /// <summary>İstifadəçinin bu kateqoriyada baxılmamış sorğusu varmı.</summary>
    Task<bool> HasPendingAsync(Guid userId, CategoryId categoryId, CancellationToken cancellationToken);

    /// <summary>İstifadəçinin bütün sorğuları, ən yenilər əvvəl.</summary>
    Task<IReadOnlyList<VerificationRequest>> ListForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Admin növbəsi: status/kateqoriya filtri, ən köhnələr əvvəl (növbə ədalətli olsun).</summary>
    Task<PagedResult<VerificationRequest>> ListForAdminAsync(
        VerificationStatus? status, CategoryId? categoryId, PageRequest page, CancellationToken cancellationToken);
}

/// <summary><c>identityHashes</c>: FIN hash-i ↔ kateqoriya ↔ hesab.</summary>
public interface IIdentityHashRepository : IRepository<IdentityHash, IdentityHashId>
{
    /// <summary>Bu kateqoriyada bu FIN hash-inə bağlanmış silinməmiş qeyd.</summary>
    Task<IdentityHash?> FindAsync(string finHash, CategoryId categoryId, CancellationToken cancellationToken);
}

/// <summary>Müddəti yaxınlaşan təsdiq: istifadəçi, kateqoriya, bitmə anı.</summary>
public sealed record DueVerificationReminder(Guid UserId, Guid CategoryId, DateTimeOffset ValidUntil);

/// <summary>Etibarlılıq xatırlatmaları üçün <c>users.verifiedCategories[]</c> üzərində atomik əməliyyatlar.</summary>
public interface IVerificationReminderStore
{
    /// <summary>Müddəti (<paramref name="now"/>, <paramref name="until"/>] aralığında bitən və bu müddət üçün hələ xatırladılmamış təsdiqlər.</summary>
    Task<IReadOnlyList<DueVerificationReminder>> FindDueAsync(DateTimeOffset now, DateTimeOffset until, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Xatırlatmanı bu <c>validUntil</c> üçün göndərilmiş kimi atomik qeyd edir (<c>reminderSentFor</c>). Başqa instans artıq qeyd edibsə və ya
    /// müddət dəyişibsə <c>false</c> — göndərmə yalnız <c>true</c> olanda olur (eyni dövr üçün təkrar yoxdur).
    /// </summary>
    Task<bool> TryMarkSentAsync(DueVerificationReminder reminder, CancellationToken cancellationToken);
}
