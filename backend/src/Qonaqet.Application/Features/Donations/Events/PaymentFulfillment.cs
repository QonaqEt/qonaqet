using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Gifts.Events;
using Qonaqet.Domain.Gifts;
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
public sealed class PaymentFulfillment(IOfferRepository offers, IPoolDonationRepository poolDonations, GiftFulfillment gifts)
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
            case PaymentPurpose.Pool:
            case PaymentPurpose.Campaign:
                return await ConfirmPoolDonationAsync(paid, cancellationToken).ConfigureAwait(false);
            case PaymentPurpose.Gift:
                // Pul təhvildə biznesə kredit kimi düşür (GiftRedeemed → RedemptionCredit + komissiya); burada yalnız Scheduled + link.
                await gifts.OnPaidAsync(GiftId.From(paid.ReferenceId), paid.At, cancellationToken).ConfigureAwait(false);
                return [];
            default:
                // Digər məqsədlər öz epic-lərində qoşulur (korporativ — E10, partnyor — E13).
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
        else if (unpaid.Purpose is PaymentPurpose.Pool or PaymentPurpose.Campaign)
        {
            await poolDonations.TryCancelPendingAsync(PoolDonationId.From(unpaid.ReferenceId), cancellationToken).ConfigureAwait(false);
        }
        else if (unpaid.Purpose == PaymentPurpose.Gift)
        {
            await gifts.OnUnpaidAsync(GiftId.From(unpaid.ReferenceId), unpaid.At, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Hovuz/kampaniya ianəsi: Mongo tranzaksiyasında ianə təsdiqi + hovuz balansı; ledger-ə <c>PoolDeposit</c>. Yazının source ID-si ianədən
    /// törəyir, ona görə ianə əvvəlki (yarıda qalmış) emalda təsdiqlənibsə də yazı bir dəfə olur.
    /// </summary>
    private async Task<IReadOnlyList<LedgerEntry>> ConfirmPoolDonationAsync(PaidPurpose paid, CancellationToken cancellationToken)
    {
        var donation = await poolDonations.ConfirmAsync(PoolDonationId.From(paid.ReferenceId), paid.At, cancellationToken).ConfigureAwait(false);
        if (donation is not { Status: PoolDonationStatus.Confirmed })
        {
            return [];
        }

        return [LedgerEntry.Post(
            LedgerAccount.Pool(donation.PoolId.Value), LedgerEntryType.PoolDeposit, donation.AmountQepik, nameof(PoolDonation), donation.Id.Value,
            donation.Id.Value, "deposit", donation.ConfirmedAt ?? paid.At)];
    }
}
