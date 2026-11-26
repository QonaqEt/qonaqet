using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Domain.Integration;
using Qonaqet.Domain.Ledger;
using Qonaqet.Infrastructure.Persistence.Postgres.Interceptors;

namespace Qonaqet.Infrastructure.Persistence.Postgres.Repositories;

/// <summary>
/// Partnyor balansından hədiyyə üçün silinmə: bir Postgres tranzaksiyası — hesabın advisory kilidi, balans yoxlaması, <c>PartnerIssue</c>
/// yazısı və <see cref="PartnerGiftFunded"/> outbox mesajı birlikdə commit olunur.
/// </summary>
public sealed class PartnerFunding(QonaqetDbContext context, ILedgerRepository ledger, IPostgresUnitOfWork unitOfWork) : IPartnerFunding
{
    public const string IssueLeg = "partner-issue";

    public async Task<bool> TryFundGiftAsync(Guid clientId, Guid giftId, long amountQepik, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var account = LedgerAccount.PartnerClient(clientId);
        await unitOfWork.BeginAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ledger.LockAccountAsync(account, cancellationToken).ConfigureAwait(false);
            if (await ledger.BalanceAsync(account, cancellationToken).ConfigureAwait(false) < amountQepik)
            {
                await unitOfWork.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            ledger.Add(LedgerEntry.Post(account, LedgerEntryType.PartnerIssue, -amountQepik, "Gift", giftId, giftId, IssueLeg, at));
            context.OutboxMessages.Add(OutboxInterceptor.ToMessage(new PartnerGiftFunded(giftId, clientId, amountQepik, at)));
            await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }
}
