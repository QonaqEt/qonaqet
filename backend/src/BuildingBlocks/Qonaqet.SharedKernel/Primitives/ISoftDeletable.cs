namespace Qonaqet.SharedKernel;

/// <summary>
/// Soft delete sahələri. Heç bir qeyd fiziki silinmir (<c>CLAUDE.md</c> → "Soft delete"):
/// repository və EF qlobal filtri <see cref="IsDeleted"/> = <c>false</c> şərtini hər sorğuya əlavə edir.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; }

    DateTimeOffset? DeletedAt { get; }

    Guid? DeletedBy { get; }

    string? DeleteReason { get; }
}

/// <summary>
/// Append-only aqreqat/entity marker-i (məs. <c>giftRedemptions</c>, <c>ledger_entries</c>, <c>audit_logs</c>).
/// Belə qeydlər heç vaxt silinmir, soft delete də olunmur; düzəliş əks qeydlə edilir.
/// </summary>
public interface IAppendOnly;
