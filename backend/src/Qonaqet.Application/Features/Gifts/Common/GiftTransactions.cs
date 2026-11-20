using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Domain.Gifts;

namespace Qonaqet.Application.Features.Gifts.Common;

/// <summary>
/// Pipeline-dan kənar (job, consumer, təkrar cəhdli əmr) hədiyyə yazısı: bir Mongo tranzaksiyasında versiya şərti ilə əvəzləmə, əlavə yazılar
/// (məs. təhvil qeydi, hovuz balansı) və aqreqatın domen hadisələri outbox-a. Başqası hədiyyəni artıq dəyişibsə heç nə yazılmır (<c>false</c>).
/// </summary>
public sealed class GiftTransactions(IUnitOfWork unitOfWork, IGiftRepository gifts, IOutbox outbox)
{
    public async Task<bool> SaveAsync(
        Gift gift, long expectedVersion, CancellationToken cancellationToken, Func<CancellationToken, Task>? alsoInTransaction = null)
    {
        ArgumentNullException.ThrowIfNull(gift);
        await unitOfWork.BeginAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!await gifts.TryReplaceAsync(gift, expectedVersion, cancellationToken).ConfigureAwait(false))
            {
                await unitOfWork.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            if (alsoInTransaction is not null)
            {
                await alsoInTransaction(cancellationToken).ConfigureAwait(false);
            }

            var events = gift.DequeueDomainEvents();
            if (events.Count > 0)
            {
                await outbox.AddAsync(events, cancellationToken).ConfigureAwait(false);
            }

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
