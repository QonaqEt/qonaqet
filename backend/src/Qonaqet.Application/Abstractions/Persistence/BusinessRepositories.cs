using Qonaqet.Application.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Catalog;

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

    /// <summary>Biznes silinəndə bütün üzvlükləri <see cref="BusinessCascade.DeleteReason"/> ilə soft delete edir.</summary>
    Task<long> CascadeDeleteForBusinessAsync(BusinessId businessId, Guid? userId, CancellationToken cancellationToken);

    /// <summary>Kaskadla silinmiş üzvlükləri bərpa edir.</summary>
    Task<long> CascadeRestoreForBusinessAsync(BusinessId businessId, Guid? userId, CancellationToken cancellationToken);
}

/// <summary><c>catalogItems</c>.</summary>
public interface ICatalogItemRepository : IRepository<CatalogItem, CatalogItemId>
{
    /// <summary>Biznesin elementləri qrup və başlıq üzrə sıralı; <paramref name="activeOnly"/> yalnız aktivləri verir.</summary>
    Task<IReadOnlyList<CatalogItem>> ListForBusinessAsync(BusinessId businessId, bool activeOnly, CancellationToken cancellationToken);

    /// <summary>Biznes silinəndə bütün elementləri <see cref="BusinessCascade.DeleteReason"/> ilə soft delete edir.</summary>
    Task<long> CascadeDeleteForBusinessAsync(BusinessId businessId, Guid? userId, CancellationToken cancellationToken);

    /// <summary>Kaskadla silinmiş elementləri bərpa edir (sahibin ayrıca sildikləri silinmiş qalır).</summary>
    Task<long> CascadeRestoreForBusinessAsync(BusinessId businessId, Guid? userId, CancellationToken cancellationToken);
}
