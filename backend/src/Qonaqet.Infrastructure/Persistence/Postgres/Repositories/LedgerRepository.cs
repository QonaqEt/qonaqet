using Microsoft.EntityFrameworkCore;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Common;
using Qonaqet.Domain.Ledger;

namespace Qonaqet.Infrastructure.Persistence.Postgres.Repositories;

/// <summary><c>ledger_entries</c> EF Core üzərində. Balans sorğusu <c>(account_type, account_id, created_at)</c> indeksindən istifadə edir.</summary>
public sealed class LedgerRepository(QonaqetDbContext context) : ILedgerRepository
{
    public void Add(LedgerEntry entry) => context.Set<LedgerEntry>().Add(entry);

    public async Task<long> BalanceAsync(LedgerAccount account, CancellationToken cancellationToken) =>
        await context.Set<LedgerEntry>().AsNoTracking()
            .Where(e => e.AccountType == account.Type && e.AccountId == account.Id)
            .SumAsync(e => e.Amount, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyDictionary<Guid, long>> BalancesAsync(LedgerAccountType accountType, CancellationToken cancellationToken) =>
        await context.Set<LedgerEntry>().AsNoTracking()
            .Where(e => e.AccountType == accountType)
            .GroupBy(e => e.AccountId)
            .Select(g => new { g.Key, Sum = g.Sum(e => e.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Sum, cancellationToken).ConfigureAwait(false);

    public async Task<PagedResult<LedgerEntry>> ListAsync(LedgerAccount account, PageRequest page, CancellationToken cancellationToken)
    {
        var query = context.Set<LedgerEntry>().AsNoTracking().Where(e => e.AccountType == account.Type && e.AccountId == account.Id);
        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PagedResult<LedgerEntry>(items, total);
    }
}
