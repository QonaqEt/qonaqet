using Qonaqet.Application.Common;
using Qonaqet.Domain.Categories;

namespace Qonaqet.Application.Abstractions.Persistence;

/// <summary><c>categories</c>.</summary>
public interface ICategoryRepository : IRepository<Category, CategoryId>
{
    /// <summary>Kod silinməmiş bir kateqoriyada işlənirmi (partial unikal indeks ilə eyni qayda).</summary>
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    /// <summary>Aktiv kateqoriyalar <c>sortOrder</c>, sonra ad üzrə (mobil və veb UI bütün kateqoriya ekranlarını bundan qurur).</summary>
    Task<IReadOnlyList<Category>> ListActiveAsync(CancellationToken cancellationToken);

    /// <summary>Admin siyahısı (deaktivlər də), <c>sortOrder</c> üzrə; <paramref name="includeDeleted"/> silinmişləri də göstərir.</summary>
    Task<PagedResult<Category>> ListForAdminAsync(bool includeDeleted, PageRequest page, CancellationToken cancellationToken);
}

/// <summary>
/// Kateqoriyaya başqa kolleksiyalardan olan istinadlar (təsdiqlər, təkliflər, kampaniyalar). Kateqoriya yalnız
/// <c>categoryId</c> ilə istinad olunur; bu oxucu kodu dəyişmə və silmə qaydaları üçündür.
/// </summary>
public interface ICategoryUsageReader
{
    /// <summary>Kateqoriya istifadədədirmi: ona aid təsdiq sorğusu, təsdiqlənmiş istifadəçi, təklif və ya kampaniya varmı.</summary>
    Task<bool> IsInUseAsync(CategoryId categoryId, CancellationToken cancellationToken);
}
