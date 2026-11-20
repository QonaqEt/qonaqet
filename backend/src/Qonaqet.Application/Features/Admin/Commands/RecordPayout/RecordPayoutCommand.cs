using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Ledger;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;
using Qonaqet.SharedKernel.Validation;

namespace Qonaqet.Application.Features.Admin.Commands.RecordPayout;

/// <summary>
/// <c>POST admin/payouts</c>: admin biznesə bank köçürməsini qeyd edir. Bir Postgres tranzaksiyasında: hesab kilidi → balans (ledger <c>SUM</c>)
/// yoxlanışı → <c>payouts</c> sətri + <c>Payout</c> ledger yazısı (−məbləğ) + audit. Balansı aşan payout rədd edilir; <c>Idempotency-Key</c> məcburidir.
/// </summary>
[RequiresRole(RoleNames.Admin)]
public sealed record RecordPayoutCommand(BusinessId BusinessId, long AmountQepik, string BankReference)
    : ICommand<Guid>, IPostgresCommand, IIdempotentCommand;

public sealed class RecordPayoutValidator : Validator<RecordPayoutCommand>
{
    public RecordPayoutValidator()
    {
        Rule("amountQepik", x => x.AmountQepik > 0, "invalid_amount", "Məbləğ müsbət olmalıdır.");
        RuleFor<string?>(x => x.BankReference).NotEmpty().MaxLength(Payout.MaxBankReferenceLength);
    }
}

public sealed class RecordPayoutHandler(IBusinessRepository businesses, ILedgerRepository ledger, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<RecordPayoutCommand, Guid>
{
    public async Task<Result<Guid>> Handle(RecordPayoutCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await businesses.GetAsync(request.BusinessId, cancellationToken).ConfigureAwait(false) is null)
        {
            return BusinessErrors.NotFound;
        }

        var account = LedgerAccount.Business(request.BusinessId.Value);
        await ledger.LockAccountAsync(account, cancellationToken).ConfigureAwait(false);
        var balance = await ledger.BalanceAsync(account, cancellationToken).ConfigureAwait(false);
        if (request.AmountQepik > balance)
        {
            return LedgerErrors.PayoutExceedsBalance;
        }

        var payout = Payout.Record(request.BusinessId.Value, request.AmountQepik, request.BankReference, currentUser.ActorId!.Value, clock.UtcNow);
        ledger.AddPayout(payout);
        return payout.Id.Value;
    }
}
