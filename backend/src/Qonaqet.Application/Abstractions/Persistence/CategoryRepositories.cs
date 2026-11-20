using Qonaqet.Application.Common;
using Qonaqet.Domain.Categories;
using Qonaqet.Domain.Social;

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

    /// <summary>Soft delete olunmuş kateqoriya (bərpadan əvvəl kod toqquşmasını yoxlamaq üçün); silinməyibsə <c>null</c>.</summary>
    Task<Category?> GetDeletedAsync(CategoryId id, CancellationToken cancellationToken);
}

/// <summary><c>pools</c>. Pul hərəkətləri E08-dədir; burada yalnız yaradılma və kaskad.</summary>
public interface IPoolRepository : IRepository<Pool, PoolId>
{
    /// <summary>Kateqoriyanın default (kampaniyasız) hovuzu.</summary>
    Task<Pool?> GetDefaultForCategoryAsync(CategoryId categoryId, CancellationToken cancellationToken);

    /// <summary>Kateqoriya silinəndə onun hovuzlarını <see cref="CategoryCascade.DeleteReason"/> ilə soft delete edir.</summary>
    Task<long> CascadeDeleteForCategoryAsync(CategoryId categoryId, Guid? userId, CancellationToken cancellationToken);

    /// <summary>Kaskadla silinmiş hovuzları bərpa edir.</summary>
    Task<long> CascadeRestoreForCategoryAsync(CategoryId categoryId, Guid? userId, CancellationToken cancellationToken);

    /// <summary>Bütün (silinməmiş) hovuzların balansları — gecəlik ledger uzlaşdırması üçün.</summary>
    Task<IReadOnlyList<PoolBalance>> ListBalancesAsync(CancellationToken cancellationToken);
}

/// <summary>Hovuzun əməliyyat balansı (Mongo). Ledger-də hovuz hesabının cəmi <see cref="Available"/>-a bərabər olmalıdır.</summary>
public sealed record PoolBalance(PoolId PoolId, long BalanceQepik, long ReservedQepik)
{
    /// <summary>Rezervdən kənar balans: ledger-də <c>PoolDeposit</c> (+), <c>PoolReserve</c> (−), <c>PoolRelease</c> (+) … cəmi.</summary>
    public long Available => BalanceQepik - ReservedQepik;
}

/// <summary>Kateqoriyanın silinməsinə mane olan istinadlar (hər biri ayrıca sayılır).</summary>
public sealed record CategoryDeleteBlockers(bool ActiveOffers, bool ActiveCampaigns, bool PoolBalance, bool ValidVerifications)
{
    public bool Any => ActiveOffers || ActiveCampaigns || PoolBalance || ValidVerifications;

    /// <summary>Sabit səbəb kodları: <c>active_offers</c>, <c>active_campaigns</c>, <c>pool_balance</c>, <c>valid_verifications</c>.</summary>
    public IReadOnlyList<string> Reasons
    {
        get
        {
            var reasons = new List<string>();
            if (ActiveOffers)
            {
                reasons.Add("active_offers");
            }

            if (ActiveCampaigns)
            {
                reasons.Add("active_campaigns");
            }

            if (PoolBalance)
            {
                reasons.Add("pool_balance");
            }

            if (ValidVerifications)
            {
                reasons.Add("valid_verifications");
            }

            return reasons;
        }
    }
}

/// <summary>
/// Kateqoriyaya başqa kolleksiyalardan olan istinadlar (təsdiqlər, təkliflər, kampaniyalar). Kateqoriya yalnız
/// <c>categoryId</c> ilə istinad olunur; bu oxucu kodu dəyişmə və silmə qaydaları üçündür.
/// </summary>
public interface ICategoryUsageReader
{
    /// <summary>Kateqoriya istifadədədirmi: ona aid təsdiq sorğusu, təsdiqlənmiş istifadəçi, təklif və ya kampaniya varmı.</summary>
    Task<bool> IsInUseAsync(CategoryId categoryId, CancellationToken cancellationToken);

    /// <summary>
    /// Silməyə mane olanlar: aktiv təklif (aktiv və ya qalığı olan), aktiv/qaralama kampaniya, sıfırdan böyük hovuz balansı
    /// (və ya rezerv) və <paramref name="now"/> anında etibarlı təsdiq. Müddəti bitmiş təsdiq mane olmur.
    /// </summary>
    Task<CategoryDeleteBlockers> GetDeleteBlockersAsync(CategoryId categoryId, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>Kateqoriyanın kaskad soft delete-inin sabit səbəbi.</summary>
public static class CategoryCascade
{
    public const string DeleteReason = "category_deleted";
}
