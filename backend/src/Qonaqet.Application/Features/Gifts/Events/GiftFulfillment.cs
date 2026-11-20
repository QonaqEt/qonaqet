using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Domain.Gifts;

namespace Qonaqet.Application.Features.Gifts.Events;

/// <summary>
/// Hədiyyə ödənişinin nəticəsi (<c>PaymentFulfillment</c>-dən çağırılır, sessiyasız və idempotent): təsdiqdə <c>AwaitingPayment</c> →
/// <c>Scheduled</c> və yeni link token-i; ödənilməyəndə <c>Cancelled</c>. Hər keçid versiya şərti ilə yazılır, ona görə təkrar çatdırılan
/// hadisə ikinci token yaratmır (statusu artıq dəyişmiş hədiyyəyə toxunulmur).
/// </summary>
public sealed class GiftFulfillment(IGiftRepository gifts, IGiftLinkTokens links)
{
    public async Task<bool> OnPaidAsync(GiftId giftId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var gift = await gifts.GetAsync(giftId, cancellationToken).ConfigureAwait(false);
        if (gift is null)
        {
            return false;
        }

        var version = gift.Version;
        if (!gift.ConfirmPaid(links.Issue().Secret, at))
        {
            return false;
        }

        return await gifts.TryReplaceAsync(gift, version, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> OnUnpaidAsync(GiftId giftId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var gift = await gifts.GetAsync(giftId, cancellationToken).ConfigureAwait(false);
        if (gift is null)
        {
            return false;
        }

        var version = gift.Version;
        return gift.CancelUnpaid(at) && await gifts.TryReplaceAsync(gift, version, cancellationToken).ConfigureAwait(false);
    }
}
