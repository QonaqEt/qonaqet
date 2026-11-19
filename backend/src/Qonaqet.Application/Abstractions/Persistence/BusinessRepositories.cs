using Qonaqet.Application.Common;
using Qonaqet.Domain.Businesses;

namespace Qonaqet.Application.Abstractions.Persistence;

public interface IBusinessRepository : IRepository<Business, BusinessId>
{
    /// <summary>Silinməmiş biznes slug üzrə.</summary>
    Task<Business?> GetBySlugAsync(string slug, CancellationToken cancellationToken);

    /// <summary>Slug silinməmiş bir biznesdə işlənirmi (partial unikal indeks ilə eyni qayda).</summary>
    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken);

    /// <summary>VÖEN silinməmiş bir biznesdə işlənirmi.</summary>
    Task<bool> VoenExistsAsync(string voen, CancellationToken cancellationToken);

    /// <summary>İstifadəçinin baxılmamış (<see cref="BusinessStatus.Pending"/>) müraciəti varmı.</summary>
    Task<bool> HasPendingApplicationAsync(Guid applicantUserId, CancellationToken cancellationToken);

    /// <summary>Admin siyahısı: status filtri, ən yenilər əvvəl; <paramref name="includeDeleted"/> silinmişləri də göstərir.</summary>
    Task<PagedResult<Business>> ListForAdminAsync(BusinessStatus? status, bool includeDeleted, PageRequest page, CancellationToken cancellationToken);
}

/// <summary><c>businessMemberships</c>: kim hansı biznesdə sahib/işçidir. Silinmiş (çıxarılmış) üzvlüklər görünmür.</summary>
public interface IBusinessMembershipRepository : IRepository<BusinessMembership, BusinessMembershipId>
{
    Task<BusinessMembership?> FindAsync(BusinessId businessId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Biznesin üzvləri, ən köhnələr əvvəl.</summary>
    Task<IReadOnlyList<BusinessMembership>> ListForBusinessAsync(BusinessId businessId, CancellationToken cancellationToken);

    /// <summary>İstifadəçinin bütün üzvlükləri, ən köhnələr əvvəl.</summary>
    Task<IReadOnlyList<BusinessMembership>> ListForUserAsync(Guid userId, CancellationToken cancellationToken);
}
