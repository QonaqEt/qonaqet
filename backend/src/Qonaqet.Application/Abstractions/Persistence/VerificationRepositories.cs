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
}

/// <summary><c>identityHashes</c>: FIN hash-i ↔ kateqoriya ↔ hesab.</summary>
public interface IIdentityHashRepository : IRepository<IdentityHash, IdentityHashId>
{
    /// <summary>Bu kateqoriyada bu FIN hash-inə bağlanmış silinməmiş qeyd.</summary>
    Task<IdentityHash?> FindAsync(string finHash, CategoryId categoryId, CancellationToken cancellationToken);
}
