using Qonaqet.Application.Abstractions.Jobs;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Gifts.Common;
using Qonaqet.Domain.Gifts;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Gifts.Jobs;

/// <summary>
/// Hədiyyələrin müddəti (Worker, 15 dəqiqədə bir):
/// <list type="number">
/// <item>Müddətin bitməsinə 7 gün qalmış alıcıya bir dəfə xatırlatma (push və ya email); qeyd versiya şərti ilə yazılır, ona görə iki instans
/// eyni hədiyyəyə iki dəfə xatırlatmır.</item>
/// <item>Müddəti bitən hədiyyə <c>Expired</c> olur; istifadə olunmamış qalıq <c>expiryAction</c>-a görə: <c>RefundToPayer</c> → ödəyiciyə qismən
/// refund (sonra <c>Refunded</c>), <c>DonateToPool</c> → eyni Mongo tranzaksiyasında hovuz balansı artır və <c>GiftExpiryDonated</c> outbox-a
/// düşür (ledger-də <c>GiftExpiryDonation</c>).</item>
/// </list>
/// Hər keçid versiya şərti ilə götürülür → job idempotentdir.
/// </summary>
public sealed class ExpireGiftsJob(
    IGiftRepository gifts, IPoolRepository pools, GiftTransactions transactions, GiftRefunder refunder, GiftNotifications notifications, IClock clock)
    : IScheduledJob
{
    public const string Name = "gift-expiry";

    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    public const int BatchSize = 200;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var processed = 0;

        foreach (var gift in await gifts.ListDueForReminderAsync(now, now + GiftRules.ExpiryReminderWindow, BatchSize, cancellationToken).ConfigureAwait(false))
        {
            var version = gift.Version;
            if (gift.MarkExpiryReminderSent(now) && await gifts.TryReplaceAsync(gift, version, cancellationToken).ConfigureAwait(false))
            {
                await notifications.ExpiringAsync(gift, cancellationToken).ConfigureAwait(false);
                processed++;
            }
        }

        foreach (var gift in await gifts.ListDueForExpiryAsync(now, BatchSize, cancellationToken).ConfigureAwait(false))
        {
            if (await ExpireAsync(gift, now, cancellationToken).ConfigureAwait(false))
            {
                processed++;
            }
        }

        return processed;
    }

    private async Task<bool> ExpireAsync(Gift gift, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var version = gift.Version;
        gift.Expire(now);
        var donation = gift.DomainEvents.OfType<GiftExpiryDonated>().SingleOrDefault();
        var saved = await transactions.SaveAsync(
            gift, version, cancellationToken,
            donation is null
                ? null
                : async ct =>
                {
                    if (!await pools.CreditAsync(donation.PoolId, donation.AmountQepik, ct).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException($"Hovuz {donation.PoolId.Value} tapılmadı.");
                    }
                }).ConfigureAwait(false);
        if (!saved)
        {
            // Başqa instans artıq bitirdi və ya hədiyyə elə indi təhvil verildi.
            return false;
        }

        if (gift.RefundDueQepik > 0)
        {
            // Provayder əlçatmazdırsa refund gözləyir; gift-refunds job-u təkrarlayır.
            await refunder.RefundAsync(gift, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }
}
