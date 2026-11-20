using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Common.Errors;

/// <summary>Ledger və payout-ların sabit xəta kodları.</summary>
public static class LedgerErrors
{
    public static readonly Error PayoutExceedsBalance = Error.Conflict("payout_exceeds_balance", "Payout biznesin balansını aşır.");
}
