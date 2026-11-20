using Qonaqet.Application.Abstractions.Messaging;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Gifts.Common;
using Qonaqet.Domain.Gifts;
using Qonaqet.SharedKernel;
using Qonaqet.SharedKernel.Messaging;

namespace Qonaqet.Application.Features.Gifts.Commands.OpenPublicGift;

/// <summary>
/// <c>GET public/gifts/{token}</c> (anonim, rate-limitli): linkin sahibi hədiyyəni görür. İlk açılış statusu <c>Opened</c> edir və göndərənə
/// "Hədiyyənizə baxıldı 🎉" bildirişi gedir — buna görə sorğu yox, əmrdir. Token bearer-dir: yalnız hash ilə axtarılır, heç vaxt loglanmır.
/// Hələ çatdırılmamış, ləğv olunmuş və ya tanınmayan link eyni <c>gift_not_found</c> alır.
/// </summary>
[PublicRequest]
public sealed record OpenPublicGiftCommand(string Token) : ICommand<PublicGiftDto>, INonTransactionalCommand
{
    /// <summary>Token heç vaxt loga düşməsin (record-un default <c>ToString</c>-i onu yazardı).</summary>
    public override string ToString() => nameof(OpenPublicGiftCommand);
}

public sealed class OpenPublicGiftHandler(
    IGiftRepository gifts, IGiftLinkTokens links, GiftPresenter presenter, GiftNotifications notifications, IClock clock)
    : ICommandHandler<OpenPublicGiftCommand, PublicGiftDto>
{
    public async Task<Result<PublicGiftDto>> Handle(OpenPublicGiftCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var gift = await GiftLinks.FindVisibleAsync(gifts, links, request.Token, cancellationToken).ConfigureAwait(false);
        if (gift is null)
        {
            return GiftErrors.NotFound;
        }

        var version = gift.Version;
        if (gift.Open(clock.UtcNow) && await gifts.TryReplaceAsync(gift, version, cancellationToken).ConfigureAwait(false))
        {
            // Yalnız qalib gələn ilk açılış bildiriş göndərir; anonim göndərən də xəbər alır.
            await notifications.OpenedAsync(gift, cancellationToken).ConfigureAwait(false);
        }

        return await presenter.PresentAsync(gift, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Link token-i ilə hədiyyənin tapılması (ictimai səhifə və "Hesabıma əlavə et").</summary>
public static class GiftLinks
{
    /// <summary>Token-ə uyğun, alıcıya artıq çatdırılmış hədiyyə; əks halda <c>null</c>.</summary>
    public static async Task<Gift?> FindVisibleAsync(IGiftRepository gifts, IGiftLinkTokens links, string? token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gifts);
        ArgumentNullException.ThrowIfNull(links);
        if (links.Hash(token) is not { } hash)
        {
            return null;
        }

        var gift = await gifts.FindByLinkHashAsync(hash, cancellationToken).ConfigureAwait(false);
        return gift is null || gift.DeliveredAt is null || gift.Status == GiftStatus.Cancelled ? null : gift;
    }
}
