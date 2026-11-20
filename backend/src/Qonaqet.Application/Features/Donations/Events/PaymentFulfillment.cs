using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Domain.Ledger;
using Qonaqet.Domain.Payments;
using Qonaqet.Domain.Social;

namespace Qonaqet.Application.Features.Donations.Events;

/// <summary>Təsdiqlənmiş/bağlanmış ödənişin məqsədi (Postgres hadisəsindən).</summary>
public sealed record PaidPurpose(Guid PaymentId, PaymentPurpose Purpose, Guid ReferenceId, long Amount, DateTimeOffset At);

/// <summary>
/// <c>PaymentConfirmed</c> / <c>PaymentClosedUnpaid</c> hadisələrinin məqsəd üzrə yerinə yetirilməsi (Mongo tərəfi). Hər addım idempotentdir
/// (atomik status keçidi); qaytarılan ledger yazılarını consumer inbox ilə eyni Postgres tranzaksiyasında yazır. Ledger yazılarının
/// <c>source_event_id</c>-si obyektdən (ianədən) törəyir, ona görə təkrar çatdırılma və ya Mongo-dan sonra yarıda qalan emal ikinci yazı yaratmır.
/// </summary>
public sealed class PaymentFulfillment(IOfferRepository offers)
{
    /// <summary>Ödəniş təsdiqləndi: məqsədin obyektini aktivləşdirir; ledger yazıları (varsa) qaytarılır.</summary>
    public async Task<IReadOnlyList<LedgerEntry>> OnPaidAsync(PaidPurpose paid, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paid);
        switch (paid.Purpose)
        {
            case PaymentPurpose.Remote:
                // Pul təhvildə biznesə kredit kimi düşür (ClaimRedeemed → RedemptionCredit + komissiya); burada yalnız aktivləşdirmə.
                await offers.TryActivateRemoteAsync(OfferId.From(paid.ReferenceId), cancellationToken).ConfigureAwait(false);
                return [];
            default:
                // Digər məqsədlər öz epic-lərində qoşulur (hədiyyə — E09, korporativ — E10, partnyor — E13).
                return [];
        }
    }

    /// <summary>Ödəniş uğursuz oldu/bitdi: gözləyən obyekt bağlanır.</summary>
    public async Task OnUnpaidAsync(PaidPurpose unpaid, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unpaid);
        if (unpaid.Purpose == PaymentPurpose.Remote)
        {
            await offers.TryCloseUnpaidRemoteAsync(OfferId.From(unpaid.ReferenceId), cancellationToken).ConfigureAwait(false);
        }
    }
}
