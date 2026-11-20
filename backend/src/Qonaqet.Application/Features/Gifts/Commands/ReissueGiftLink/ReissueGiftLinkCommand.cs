using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Gifts.Commands.RevealGiftLink;
using Qonaqet.Domain.Gifts;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Gifts.Commands.ReissueGiftLink;

/// <summary>
/// <c>POST gifts/{id}/reissue-link</c>: yalnız göndərən. Yeni token yaradılır, köhnə hash əvəz olunur (köhnə link dərhal işləmir), yeni link
/// cavabda bir dəfə qaytarılır. Hədiyyə aktiv olmalıdır (ödənilib, yekunlaşmayıb).
/// </summary>
[RequiresActiveAccount]
public sealed record ReissueGiftLinkCommand(GiftId GiftId) : ICommand<GiftShareLinkDto>, INonTransactionalCommand;

public sealed class ReissueGiftLinkHandler(IGiftRepository gifts, IGiftLinkTokens links, IAppUrls urls, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<ReissueGiftLinkCommand, GiftShareLinkDto>
{
    public async Task<Result<GiftShareLinkDto>> Handle(ReissueGiftLinkCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        var gift = await gifts.GetAsync(request.GiftId, cancellationToken).ConfigureAwait(false);
        if (gift is null || gift.SenderUserId != userId)
        {
            return GiftErrors.NotFound;
        }

        if (!gift.IsActive)
        {
            return GiftErrors.LinkUnavailable;
        }

        var issue = links.Issue();
        var version = gift.Version;
        gift.ReissueLink(issue.Secret, clock.UtcNow, userId);
        if (!await gifts.TryReplaceAsync(gift, version, cancellationToken).ConfigureAwait(false))
        {
            return GiftErrors.Changed;
        }

        return new GiftShareLinkDto(gift.Id.Value, urls.GiftLink(issue.Token));
    }
}
