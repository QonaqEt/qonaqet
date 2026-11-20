using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Ledger;

/// <summary>
/// Gecəlik uzlaşdırmanın bir icrası (Postgres <c>reconciliation_runs</c>, append-only): neçə hesab yoxlanıldı, neçəsində fərq var.
/// </summary>
public sealed class ReconciliationRun : IAppendOnly
{
    private ReconciliationRun()
    {
    }

    public Guid Id { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public int AccountsChecked { get; private set; }

    public int Mismatches { get; private set; }

    public static ReconciliationRun Record(Guid id, int accountsChecked, int mismatches, DateTimeOffset at) => new()
    {
        Id = Guard.NotEmpty(id),
        CreatedAt = Guard.Utc(at),
        AccountsChecked = accountsChecked,
        Mismatches = mismatches,
    };
}

/// <summary>
/// Uzlaşdırma fərqi (Postgres <c>reconciliation_mismatches</c>, append-only): əməliyyat balansı (Mongo) ilə ledger cəmi uyğun gəlmir.
/// <see cref="Difference"/> = əməliyyat − ledger.
/// </summary>
public sealed class ReconciliationMismatch : IAppendOnly
{
    private ReconciliationMismatch()
    {
    }

    public Guid Id { get; private set; }

    public Guid RunId { get; private set; }

    public LedgerAccountType AccountType { get; private set; }

    public Guid AccountId { get; private set; }

    public long OperationalBalance { get; private set; }

    public long LedgerBalance { get; private set; }

    public long Difference { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static ReconciliationMismatch Record(Guid runId, LedgerAccount account, long operational, long ledger, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(),
        RunId = runId,
        AccountType = account.Type,
        AccountId = account.Id,
        OperationalBalance = operational,
        LedgerBalance = ledger,
        Difference = operational - ledger,
        CreatedAt = Guard.Utc(at),
    };
}
