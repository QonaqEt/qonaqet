using Microsoft.Extensions.Logging;
using Qonaqet.Application.Abstractions.Jobs;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Domain.Ledger;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Ledger.Jobs;

/// <summary>
/// Gecəlik uzlaşdırma (Worker, gündə bir, tək icraçı): hər hovuzun Mongo-dakı əməliyyat balansını (<c>balance − reserved</c>) ledger-dəki hovuz
/// hesabının <c>SUM(amount)</c>-u ilə müqayisə edir. Hər icra <c>reconciliation_runs</c>-a, hər fərq <c>reconciliation_mismatches</c>-ə yazılır
/// (admin hesabatı) və xəbərdarlıq logu verilir (monitorinq alert-i bu loga bağlanır). Heç nəyi "düzəltmir": fərqi insan araşdırır.
/// </summary>
public sealed partial class ReconcileLedgerJob(IPoolRepository pools, ILedgerRepository ledger, IClock clock, ILogger<ReconcileLedgerJob> logger)
    : IScheduledJob
{
    public const string Name = "ledger-reconciliation";

    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var operational = await pools.ListBalancesAsync(cancellationToken).ConfigureAwait(false);
        var ledgerBalances = await ledger.BalancesAsync(LedgerAccountType.Pool, cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        var runId = Guid.CreateVersion7();

        var mismatches = new List<ReconciliationMismatch>();
        var checkedIds = new HashSet<Guid>();
        foreach (var pool in operational)
        {
            checkedIds.Add(pool.PoolId.Value);
            var inLedger = ledgerBalances.GetValueOrDefault(pool.PoolId.Value);
            if (inLedger != pool.Available)
            {
                mismatches.Add(ReconciliationMismatch.Record(runId, LedgerAccount.Pool(pool.PoolId.Value), pool.Available, inLedger, now));
            }
        }

        // Ledger-də pulu olan, amma Mongo-da (silinmiş/olmayan) hovuz da fərqdir.
        foreach (var (poolId, inLedger) in ledgerBalances.Where(b => !checkedIds.Contains(b.Key) && b.Value != 0))
        {
            mismatches.Add(ReconciliationMismatch.Record(runId, LedgerAccount.Pool(poolId), 0, inLedger, now));
        }

        var run = ReconciliationRun.Record(runId, checkedIds.Count, mismatches.Count, now);
        ledger.AddReconciliation(run, mismatches);
        await ledger.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var mismatch in mismatches)
        {
            LogMismatch(mismatch.AccountType.ToString(), mismatch.AccountId, mismatch.OperationalBalance, mismatch.LedgerBalance, mismatch.Difference);
        }

        return checkedIds.Count;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ledger uzlaşdırma fərqi: {AccountType} {AccountId} — əməliyyat {Operational}, ledger {Ledger}, fərq {Difference}")]
    private partial void LogMismatch(string accountType, Guid accountId, long operational, long ledger, long difference);
}
