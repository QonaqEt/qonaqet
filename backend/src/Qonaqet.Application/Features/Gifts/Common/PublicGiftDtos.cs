using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Common;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Gifts;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Gifts.Common;

/// <summary>Hədiyyədəki məhsul: ad, ümumi və qalan say.</summary>
public sealed record GiftItemDto(Guid CatalogItemId, string Title, long UnitPriceQepik, int Qty, int RemainingQty);

/// <summary>Hədiyyənin işləndiyi biznes (xəritə üçün): ictimai məlumat.</summary>
public sealed record GiftBusinessDto(Guid Id, string Name, string Slug, string Address, double Latitude, double Longitude);

/// <summary>
/// İctimai hədiyyə səhifəsi (<c>/g/{token}</c>): temalı kart, göndərənin göstərilən adı (anonimdə "Anonim dost"), mesaj, məzmun, qalıq,
/// təhvil kodları (yalnız istifadə oluna bilərkən), biznes(lər) və müddət. Alıcının və göndərənin email-i, ID-ləri yoxdur.
/// </summary>
public sealed record PublicGiftDto(
    GiftTheme Theme,
    string SenderName,
    string RecipientName,
    string? Message,
    GiftKind Kind,
    GiftScope Scope,
    IReadOnlyList<GiftItemDto> Items,
    long AmountQepik,
    long RemainingQepik,
    GiftStatus Status,
    DateTimeOffset DeliveredAt,
    DateTimeOffset ExpiresAt,
    bool IsRedeemable,
    bool IsClaimedToAccount,
    GiftCodesDto? Codes,
    IReadOnlyList<GiftBusinessDto> Businesses);

/// <summary>Hədiyyənin alıcıya göstərilən formasını qurur (ictimai səhifə və "Hədiyyələrim").</summary>
public sealed class GiftPresenter(IBusinessRepository businesses, IBusinessSearch search, IRedemptionCodes codes, IQrCodeEncoder qr, IClock clock)
{
    /// <summary>Şəbəkə vauçerində xəritədə göstərilən biznes sayı.</summary>
    public const int NetworkBusinessLimit = 50;

    public async Task<PublicGiftDto> PresentAsync(Gift gift, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gift);
        var now = clock.UtcNow;
        var redeemable = gift.IsRedeemableAt(now);
        return new PublicGiftDto(
            gift.Theme,
            gift.SenderDisplayName,
            gift.RecipientName,
            gift.Message,
            gift.Kind,
            gift.Scope,
            Items(gift),
            gift.AmountQepik,
            gift.RemainingQepik,
            EffectiveStatus(gift, now),
            gift.DeliveredAt ?? gift.DeliverAt,
            gift.ExpiresAt,
            redeemable,
            gift.RecipientUserId is not null,
            redeemable ? GiftCodes.Present(codes, qr, gift.Id) : null,
            await BusinessesAsync(gift, cancellationToken).ConfigureAwait(false));
    }

    public static IReadOnlyList<GiftItemDto> Items(Gift gift)
    {
        ArgumentNullException.ThrowIfNull(gift);
        return [.. gift.Items.Select(i => new GiftItemDto(i.CatalogItemId.Value, i.Title, i.UnitPriceQepik, i.Qty, i.RemainingQty))];
    }

    /// <summary>Müddəti keçmiş, amma job-un hələ bağlamadığı hədiyyə <c>Expired</c> görünür.</summary>
    public static GiftStatus EffectiveStatus(Gift gift, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(gift);
        return gift.IsLive && now >= gift.ExpiresAt ? GiftStatus.Expired : gift.Status;
    }

    private async Task<IReadOnlyList<GiftBusinessDto>> BusinessesAsync(Gift gift, CancellationToken cancellationToken)
    {
        if (gift.BusinessId is { } businessId)
        {
            var business = await businesses.GetAsync(businessId, cancellationToken).ConfigureAwait(false);
            return business is null ? [] : [ToDto(business)];
        }

        var hits = await search.SearchAsync(
            new BusinessSearchCriteria(null, null, 0, null, null, AcceptsNetworkVouchers: true), PageRequest.Of(1, NetworkBusinessLimit), cancellationToken)
            .ConfigureAwait(false);
        return [.. hits.Items.Select(h => ToDto(h.Business))];
    }

    private static GiftBusinessDto ToDto(Business business) =>
        new(business.Id.Value, business.Name, business.Slug, business.Address, business.Location.Latitude, business.Location.Longitude);
}
