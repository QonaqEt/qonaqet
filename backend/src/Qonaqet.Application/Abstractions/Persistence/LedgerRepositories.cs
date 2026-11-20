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
}
