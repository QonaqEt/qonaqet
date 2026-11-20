using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Common.Errors;
using Qonaqet.Application.Features.Social.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Gifts;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Gifts.Common;

/// <summary>
/// Hədiyyənin kassada təhvili. Bir Mongo tranzaksiyasında: <c>giftRedemptions</c> qeydi, qalıq/status (versiya şərti ilə) və <c>GiftRedeemed</c>
/// outbox hadisəsi. Hədiyyə eyni anda dəyişibsə (başqa kassa), təzə vəziyyətlə <b>bir dəfə</b> təkrar cəhd olunur; seçim artıq mümkün deyilsə və
/// ya ikinci cəhd də toqquşursa <c>gift_changed</c> — qalıq heç vaxt mənfiyə düşmür. İşçi göndərəni, alıcını və mesajı görmür.
/// </summary>
public sealed class GiftRedemptionService(IGiftRepository gifts, IGiftRedemptionRepository redemptions, GiftTransactions transactions, IClock clock)
{
    public const string Label = "Hədiyyə";

    public async Task<Result<RedemptionPreviewDto>> RedeemAsync(
        Gift gift, Business business, IReadOnlyList<GiftLine>? lines, long? amountQepik, Guid staffUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gift);
        ArgumentNullException.ThrowIfNull(business);
        var current = gift;
        for (var attempt = 1; ; attempt++)
        {
            var now = clock.UtcNow;
            if (current.CheckRedeem(business.Id, business.AcceptsNetworkVouchers, lines, amountQepik, now) is { } rejection)
            {
                // Təkrar cəhddə seçimin keçməməsi paralel dəyişikliyin nəticəsidir.
                return attempt > 1 ? GiftErrors.Changed : ToError(rejection, current);
            }

            var version = current.Version;
            var redemption = current.Redeem(business.Id, business.AcceptsNetworkVouchers, lines, amountQepik, staffUserId, now);
            if (await transactions.SaveAsync(current, version, cancellationToken, ct => redemptions.AddAsync(redemption, ct)).ConfigureAwait(false))
            {
                return Preview(current, now, redemption.ValueQepik);
            }

            // Uduzan cəhdin hadisələri (GiftRedeemed) heç vaxt outbox-a düşməməlidir.
            current.DequeueDomainEvents();
            if (attempt == 2)
            {
                return GiftErrors.Changed;
            }

            // Rəqib tranzaksiyanın commit olunmasına imkan ver, sonra təzə vəziyyəti oxu.
            await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(20, 60)), cancellationToken).ConfigureAwait(false);
            current = await gifts.GetAsync(gift.Id, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("Hədiyyə yoxa çıxdı.");
        }
    }

    public static RedemptionPreviewDto Preview(Gift gift, DateTimeOffset now, long? redeemedNow = null)
    {
        ArgumentNullException.ThrowIfNull(gift);
        var status = GiftPresenter.EffectiveStatus(gift, now);
        var details = new GiftRedemptionDetailsDto(
            gift.Kind,
            gift.Scope,
            [.. gift.Items.Select(i => new GiftRedeemableItemDto(i.CatalogItemId.Value, i.Title, i.UnitPriceQepik, i.RemainingQty))],
            gift.RemainingQepik,
            redeemedNow);
        return new RedemptionPreviewDto(
            RedemptionKind.Gift, gift.Id.Value, Title(gift), status.ToString(), gift.RemainingQepik, Label, gift.ExpiresAt, gift.IsRedeemableAt(now),
            null, details);
    }

    private static string Title(Gift gift) => gift.Kind == GiftKind.Items
        ? string.Join(", ", gift.Items.Select(i => $"{i.Qty} × {i.Title}"))
        : gift.Scope == GiftScope.Network ? "Şəbəkə vauçeri" : "Vauçer";

    private static Error ToError(GiftRedeemRejection rejection, Gift gift) => rejection switch
    {
        GiftRedeemRejection.AlreadyRedeemed => SocialErrors.AlreadyRedeemed,
        GiftRedeemRejection.Expired => GiftErrors.Expired,
        GiftRedeemRejection.WrongBusiness => gift.Scope == GiftScope.Network ? GiftErrors.NetworkNotAccepted : SocialErrors.CodeNotFound,
        GiftRedeemRejection.InvalidSelection => GiftErrors.SelectionInvalid,
        GiftRedeemRejection.InsufficientBalance => GiftErrors.InsufficientBalance,
        _ => GiftErrors.NotRedeemable,
    };
}
