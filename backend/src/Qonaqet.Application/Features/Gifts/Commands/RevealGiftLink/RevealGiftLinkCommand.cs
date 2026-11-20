using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Domain.Gifts;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Gifts.Commands.RevealGiftLink;

/// <summary>
/// <c>POST gifts/{id}/share-link</c>: ödənişdən sonra göndərən paylaşma linkini <b>bir dəfə</b> götürür (WhatsApp/Telegram ilə özü göndərir).
/// İkinci çağırış <c>gift_link_already_revealed</c> alır — itirilmiş link üçün <c>reissue-link</c> yeni token verir. Token loglanmır.
/// </summary>
[RequiresActiveAccount]
public sealed record RevealGiftLinkCommand(GiftId GiftId) : ICommand<GiftShareLinkDto>, INonTransactionalCommand;

public sealed record GiftShareLinkDto(Guid GiftId, Uri ShareUrl);

public sealed class RevealGiftLinkHandler(IGiftRepository gifts, IGiftLinkTokens links, IAppUrls urls, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<RevealGiftLinkCommand, GiftShareLinkDto>
{
    public async Task<Result<GiftShareLinkDto>> Handle(RevealGiftLinkCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        var gift = await gifts.GetAsync(request.GiftId, cancellationToken).ConfigureAwait(false);
        if (gift is null || gift.SenderUserId != userId)
        {
            return GiftErrors.NotFound;
        }

        if (gift.LinkRevealedAt is not null)
        {
            return GiftErrors.LinkAlreadyRevealed;
        }

        var token = links.Unseal(gift.LinkTokenSealed);
        var version = gift.Version;
        if (token is null || !gift.RevealLink(clock.UtcNow))
        {
            return GiftErrors.LinkUnavailable;
        }

        if (!await gifts.TryReplaceAsync(gift, version, cancellationToken).ConfigureAwait(false))
        {
            // Paralel çağırış bizi qabaqladı: link yalnız bir dəfə verilir.
            return GiftErrors.LinkAlreadyRevealed;
        }

        return new GiftShareLinkDto(gift.Id.Value, urls.GiftLink(token));
    }
}
