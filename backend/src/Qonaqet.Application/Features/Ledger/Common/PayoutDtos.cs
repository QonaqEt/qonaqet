using Qonaqet.Domain.Ledger;

namespace Qonaqet.Application.Features.Ledger.Common;

public sealed record PayoutDto(Guid Id, Guid BusinessId, long AmountQepik, string BankReference, DateTimeOffset CreatedAt);

public sealed record ReconciliationMismatchDto(LedgerAccountType AccountType, Guid AccountId, long OperationalBalanceQepik, long LedgerBalanceQepik, long DifferenceQepik);

/// <summary>Son uzlaşdırma: nə vaxt, neçə hesab, hansı fərqlər.</summary>
public sealed record ReconciliationReportDto(DateTimeOffset? RanAt, int AccountsChecked, IReadOnlyList<ReconciliationMismatchDto> Mismatches);

public static class LedgerMappings
{
    public static PayoutDto ToDto(this Payout payout)
    {
        ArgumentNullException.ThrowIfNull(payout);
        return new PayoutDto(payout.Id.Value, payout.BusinessId, payout.Amount, payout.BankReference, payout.CreatedAt);
    }
}
