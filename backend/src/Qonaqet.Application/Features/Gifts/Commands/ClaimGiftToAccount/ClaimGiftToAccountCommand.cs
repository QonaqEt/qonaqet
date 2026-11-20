using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Gifts.Commands.OpenPublicGift;
using Qonaqet.Application.Features.Gifts.Common;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Gifts.Commands.ClaimGiftToAccount;

/// <summary>
/// <c>POST public/gifts/{token}/claim</c> ("Hesabıma əlavə et", email OTP ilə daxil olduqdan sonra): hədiyyə istifadəçinin hesabına bağlanır
/// (<c>recipientUserId</c>) və tətbiqdə "Hədiyyələrim"də görünür. Link işləməyə davam edir — tətbiqi olmayan valideyn üçün. Başqa hesaba
/// artıq bağlanmış hədiyyə <c>gift_already_claimed</c> alır; eyni hesab üçün təkrar çağırış uğurludur.
/// </summary>
public sealed record ClaimGiftToAccountCommand(string Token) : ICommand<ReceivedGiftDto>, INonTransactionalCommand
{
    public override string ToString() => nameof(ClaimGiftToAccountCommand);
}

public sealed class ClaimGiftToAccountHandler(
    IGiftRepository gifts, IGiftLinkTokens links, GiftPresenter presenter, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<ClaimGiftToAccountCommand, ReceivedGiftDto>
{
    private const int MaxAttempts = 3;

    public async Task<Result<ReceivedGiftDto>> Handle(ClaimGiftToAccountCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = currentUser.ActorId ?? throw new InvalidOperationException("Autentifikasiya olunmuş aktor gözlənilirdi.");
        for (var attempt = 1; ; attempt++)
        {
            var gift = await GiftLinks.FindVisibleAsync(gifts, links, request.Token, cancellationToken).ConfigureAwait(false);
            if (gift is null)
            {
                return GiftErrors.NotFound;
            }

            if (gift.RecipientUserId == userId)
            {
                return new ReceivedGiftDto(gift.Id.Value, await presenter.PresentAsync(gift, cancellationToken).ConfigureAwait(false));
            }

            var version = gift.Version;
            if (!gift.ClaimTo(userId, clock.UtcNow))
            {
                return GiftErrors.AlreadyClaimed;
            }

            if (await gifts.TryReplaceAsync(gift, version, cancellationToken).ConfigureAwait(false))
            {
                return new ReceivedGiftDto(gift.Id.Value, await presenter.PresentAsync(gift, cancellationToken).ConfigureAwait(false));
            }

            if (attempt == MaxAttempts)
            {
                return GiftErrors.Changed;
            }
        }
    }
}
