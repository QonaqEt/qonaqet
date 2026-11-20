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

    public async Task LockAccountAsync(LedgerAccount account, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Hesab kilidi yalnız açıq Postgres tranzaksiyasında işləyir.");
        }

        var key = LockKey(account);
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken).ConfigureAwait(false);
    }

    public void AddPayout(Payout payout)
    {
        ArgumentNullException.ThrowIfNull(payout);
        context.Set<Payout>().Add(payout);
        context.Set<LedgerEntry>().Add(payout.ToLedgerEntry());
    }

    public async Task<PagedResult<Payout>> ListPayoutsAsync(Guid? businessId, PageRequest page, CancellationToken cancellationToken)
    {
        var query = context.Set<Payout>().AsNoTracking();
        if (businessId is { } id)
        {
            query = query.Where(p => p.BusinessId == id);
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query.OrderByDescending(p => p.CreatedAt).Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PagedResult<Payout>(items, total);
    }

    public void AddReconciliation(ReconciliationRun run, IReadOnlyList<ReconciliationMismatch> mismatches)
    {
        context.Set<ReconciliationRun>().Add(run);
        context.Set<ReconciliationMismatch>().AddRange(mismatches);
    }

    public async Task<(ReconciliationRun Run, IReadOnlyList<ReconciliationMismatch> Mismatches)?> LatestReconciliationAsync(CancellationToken cancellationToken)
    {
        var run = await context.Set<ReconciliationRun>().AsNoTracking().OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            return null;
        }

        var mismatches = await context.Set<ReconciliationMismatch>().AsNoTracking().Where(m => m.RunId == run.Id)
            .OrderBy(m => m.AccountType).ThenBy(m => m.AccountId).ToListAsync(cancellationToken).ConfigureAwait(false);
        return (run, mismatches);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    /// <summary>Hesabdan sabit 64-bit kilid açarı (FNV-1a; scheduler-in açarlarından ayrı məkan üçün tip də daxildir).</summary>
    public static long LockKey(LedgerAccount account)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offset;
        Span<byte> bytes = stackalloc byte[17];
        bytes[0] = (byte)account.Type;
        account.Id.TryWriteBytes(bytes[1..]);
        foreach (var b in bytes)
        {
            hash = (hash ^ b) * prime;
        }

        return unchecked((long)hash);
    }
}
