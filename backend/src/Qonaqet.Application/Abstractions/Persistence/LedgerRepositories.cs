using Qonaqet.Application.Common;
using Qonaqet.Domain.Ledger;

namespace Qonaqet.Application.Abstractions.Persistence;

/// <summary>
/// <c>ledger_entries</c> (Postgres, append-only). Yazılar açıq Postgres tranzaksiyasında (əmr və ya consumer) saxlanılır; balans həmişə
/// <c>SUM(amount)</c>-dır, keşlənmir.
/// </summary>
public interface ILedgerRepository
{
    void Add(LedgerEntry entry);

    Task<long> BalanceAsync(LedgerAccount account, CancellationToken cancellationToken);

    /// <summary>Bir növ hesabların hamısının balansları (gecəlik uzlaşdırma üçün).</summary>
    Task<IReadOnlyDictionary<Guid, long>> BalancesAsync(LedgerAccountType accountType, CancellationToken cancellationToken);

    /// <summary>Hesabın yazıları, yenidən köhnəyə.</summary>
    Task<PagedResult<LedgerEntry>> ListAsync(LedgerAccount account, PageRequest page, CancellationToken cancellationToken);

    /// <summary>
    /// Hesab üzrə tranzaksiya kilidi (<c>pg_advisory_xact_lock</c>): balansı yoxlayıb çıxış yazan əmrlər (payout) ardıcıllaşır, ona görə iki eyni anda
    /// payout balansı aşa bilmir. Yalnız açıq Postgres tranzaksiyasında; commit/rollback ilə buraxılır.
    /// </summary>
    Task LockAccountAsync(LedgerAccount account, CancellationToken cancellationToken);

    void AddPayout(Payout payout);

    /// <summary>Payout-lar, yenidən köhnəyə; <paramref name="businessId"/> verilməsə hamısı.</summary>
    Task<PagedResult<Payout>> ListPayoutsAsync(Guid? businessId, PageRequest page, CancellationToken cancellationToken);

    void AddReconciliation(ReconciliationRun run, IReadOnlyList<ReconciliationMismatch> mismatches);

    /// <summary>Son uzlaşdırma icrası və onun fərqləri; heç icra olmayıbsa <c>null</c>.</summary>
    Task<(ReconciliationRun Run, IReadOnlyList<ReconciliationMismatch> Mismatches)?> LatestReconciliationAsync(CancellationToken cancellationToken);

    /// <summary>Açıq tranzaksiya yoxdursa (job) dəyişiklikləri yazır.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
