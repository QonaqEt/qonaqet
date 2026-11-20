using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Gifts.Common;
using Qonaqet.Domain.Gifts;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Gifts.Commands.CancelGift;

/// <summary>
/// <c>POST gifts/{id}/cancel</c>: göndərən hələ çatdırılmamış (<c>Scheduled</c>) hədiyyəni ləğv edir; ödəniş tam refund olunur. İdempotentdir:
/// artıq ləğv olunmuş hədiyyə üçün təkrar çağırış eyni nəticəni qaytarır (refund hələ gözləyirsə yenidən cəhd olunur).
/// </summary>
[RequiresActiveAccount]
public sealed record CancelGiftCommand(GiftId GiftId) : ICommand<SentGiftDto>, INonTransactionalCommand;

public sealed class CancelGiftHandler(
    IGiftRepository gifts, GiftTransactions transactions, GiftRefunder refunder, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<CancelGiftCommand, SentGiftDto>
{
    public async Task<Result<SentGiftDto>> Handle(CancelGiftCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        var gift = await gifts.GetAsync(request.GiftId, cancellationToken).ConfigureAwait(false);
        if (gift is null || gift.SenderUserId != userId)
        {
            return GiftErrors.NotFound;
        }

        if (gift.Status == GiftStatus.Scheduled)
        {
            var version = gift.Version;
            gift.Cancel(clock.UtcNow, userId);
            if (!await transactions.SaveAsync(gift, version, cancellationToken).ConfigureAwait(false))
            {
                // Elə bu an çatdırıldı (və ya başqa sorğu ləğv etdi): təzə vəziyyətə görə cavab.
                gift.DequeueDomainEvents();
                gift = await gifts.GetAsync(request.GiftId, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Hədiyyə yoxa çıxdı.");
            }
        }

        // Yalnız ödənişdən sonra göndərənin ləğv etdiyi hədiyyə (ödənilməyən "Cancelled" deyil).
        if (gift.Status != GiftStatus.Cancelled || gift.PaidAt is null)
        {
            return GiftErrors.NotCancellable;
        }

        if (gift.RefundDueQepik > 0)
        {
            await refunder.RefundAsync(gift, cancellationToken).ConfigureAwait(false);
            gift = await gifts.GetAsync(request.GiftId, cancellationToken).ConfigureAwait(false) ?? gift;
        }

        return GiftViews.Sent(gift, clock.UtcNow);
    }
}
