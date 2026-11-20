using Qonaqet.Domain.Gifts;

namespace Qonaqet.Application.Features.Gifts.Common;

/// <summary>Alıcının "Hədiyyələrim → Alınanlar" kartı: ictimai səhifənin məlumatı + hədiyyənin ID-si.</summary>
public sealed record ReceivedGiftDto(Guid Id, PublicGiftDto Gift);

/// <summary>
/// Göndərənin "Hədiyyələrim → Göndərilənlər" kartı: status zaman xətti (ödənildi, çatdırıldı, açıldı), məzmun və qalıq, ləğv/link imkanları.
/// Təhvil kodları burada yoxdur — onlar yalnız alıcıya aiddir.
/// </summary>
public sealed record SentGiftDto(
    Guid Id,
    GiftKind Kind,
    GiftScope Scope,
    Guid? BusinessId,
    IReadOnlyList<GiftItemDto> Items,
    long AmountQepik,
    long RemainingQepik,
    GiftStatus Status,
    GiftTheme Theme,
    string? Message,
    SenderDisplay SenderDisplay,
    string RecipientName,
    string? RecipientEmail,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset DeliverAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? OpenedAt,
    DateTimeOffset ExpiresAt,
    GiftExpiryAction ExpiryAction,
    long RefundedQepik,
    bool CanCancel,
    bool CanTakeLink,
    bool CanReissueLink);

public static class GiftViews
{
    public static SentGiftDto Sent(Gift gift, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(gift);
        return new SentGiftDto(
            gift.Id.Value, gift.Kind, gift.Scope, gift.BusinessId?.Value, GiftPresenter.Items(gift), gift.AmountQepik, gift.RemainingQepik,
            GiftPresenter.EffectiveStatus(gift, now), gift.Theme, gift.Message, gift.SenderDisplay, gift.RecipientName, gift.RecipientEmail, gift.CreatedAt,
            gift.PaidAt, gift.DeliverAt, gift.DeliveredAt, gift.OpenedAt, gift.ExpiresAt, gift.ExpiryAction, gift.RefundedQepik,
            CanCancel: gift.Status == GiftStatus.Scheduled,
            CanTakeLink: gift.IsActive && gift.LinkRevealedAt is null && gift.LinkTokenSealed is not null,
            CanReissueLink: gift.IsActive);
    }
}
