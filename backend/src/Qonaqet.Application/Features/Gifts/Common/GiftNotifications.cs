using System.Globalization;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Users.Common;
using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Users;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Gifts.Common;

/// <summary>
/// Hədiyyə bildirişləri (push + email). Bildirişlər best-effort-dur: göndərmə uğursuzluğu hədiyyənin vəziyyətini dəyişmir. Push-lara token,
/// kod və ya alıcının email-i qoyulmur — yalnız hədiyyənin ID-si (tətbiq onu öz API-si ilə açır).
/// </summary>
public sealed class GiftNotifications(
    UserPushNotifier push, IEmailTemplateRenderer templates, IEmailQueue emails, IBusinessRepository businesses, IAppUrls urls)
{
    public const string DeliveredTemplate = "gift-delivered";

    public const string RedeemedTemplate = "gift-redeemed";

    public const string ExpiringTemplate = "gift-expiring";

    /// <summary>Çatdırılma: alıcıya temalı email (link ilə) və hesabı varsa push; göndərənə "Hədiyyəniz çatdırıldı".</summary>
    public async Task DeliveredAsync(Gift gift, string? token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gift);
        if (gift.RecipientEmail is { } email && token is not null)
        {
            var model = new Dictionary<string, string?>
            {
                ["recipientName"] = gift.RecipientName,
                ["senderName"] = gift.SenderDisplayName,
                ["message"] = gift.Message ?? "🎁",
                ["contents"] = Contents(gift),
                ["where"] = await WhereAsync(gift, cancellationToken).ConfigureAwait(false),
                ["link"] = urls.GiftLink(token).ToString(),
                ["expiresAt"] = BakuDate(gift.ExpiresAt),
                ["themeToken"] = ThemeToken(gift.Theme),
            };
            await emails.EnqueueAsync(templates.Render(DeliveredTemplate, model).To(email), DeliveredTemplate, cancellationToken).ConfigureAwait(false);
        }

        if (gift.RecipientUserId is { } recipient)
        {
            await push.NotifyAsync(
                UserId.From(recipient), NotificationType.GiftReceived,
                new PushMessage("Sizə hədiyyə gəldi 🎁", $"{gift.SenderDisplayName} sizə hədiyyə göndərdi.", Data(gift)), cancellationToken).ConfigureAwait(false);
        }

        if (gift.SenderUserId is { } sender)
        {
            await push.NotifyAsync(
                UserId.From(sender), NotificationType.GiftStatus,
                new PushMessage("Hədiyyəniz çatdırıldı", $"{gift.RecipientName} üçün hədiyyəniz çatdırıldı.", Data(gift)), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>İlk açılış: göndərənə (anonim olsa da) "Hədiyyənizə baxıldı 🎉".</summary>
    public async Task OpenedAsync(Gift gift, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gift);
        if (gift.SenderUserId is { } sender)
        {
            await push.NotifyAsync(
                UserId.From(sender), NotificationType.GiftStatus,
                new PushMessage("Hədiyyənizə baxıldı 🎉", $"{gift.RecipientName} hədiyyənizi açdı.", Data(gift)), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Təhvil ("Hədiyyə istifadə edildi"): göndərənə (anonim olsa da) push; alıcıya hesabı varsa push, yoxdursa email. Biznesin adı və alıcının
    /// kimliyi göndərənə verilmir — yalnız alıcının (göndərənin özünün yazdığı) adı.
    /// </summary>
    public async Task RedeemedAsync(Gift gift, long valueQepik, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gift);
        var redeemed = Money.FromQepik(valueQepik).ToString();
        var remaining = Remaining(gift);

        if (gift.SenderUserId is { } sender)
        {
            await push.NotifyAsync(
                UserId.From(sender), NotificationType.GiftStatus,
                new PushMessage("Hədiyyə istifadə edildi", $"{gift.RecipientName} hədiyyənizdən istifadə etdi 💚", Data(gift)), cancellationToken).ConfigureAwait(false);
        }

        if (gift.RecipientUserId is { } recipient)
        {
            await push.NotifyAsync(
                UserId.From(recipient), NotificationType.GiftStatus,
                new PushMessage("Hədiyyə istifadə edildi", $"{redeemed} istifadə olundu. Qalıq: {remaining}.", Data(gift)), cancellationToken).ConfigureAwait(false);
        }
        else if (gift.RecipientEmail is { } email)
        {
            var model = new Dictionary<string, string?>
            {
                ["recipientName"] = gift.RecipientName,
                ["senderName"] = gift.SenderDisplayName,
                ["redeemed"] = redeemed,
                ["remaining"] = remaining,
            };
            await emails.EnqueueAsync(templates.Render(RedeemedTemplate, model).To(email), RedeemedTemplate, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Müddətin bitməsinə 7 gün qalmış alıcıya xatırlatma: hesabı varsa push, yoxdursa email.</summary>
    public async Task ExpiringAsync(Gift gift, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gift);
        var expiresAt = BakuDate(gift.ExpiresAt);
        if (gift.RecipientUserId is { } recipient)
        {
            await push.NotifyAsync(
                UserId.From(recipient), NotificationType.GiftExpiring,
                new PushMessage("Hədiyyənizin müddəti bitir", $"{Remaining(gift)} — {expiresAt} tarixinədək istifadə edin.", Data(gift)), cancellationToken)
                .ConfigureAwait(false);
        }
        else if (gift.RecipientEmail is { } email)
        {
            var model = new Dictionary<string, string?>
            {
                ["recipientName"] = gift.RecipientName,
                ["senderName"] = gift.SenderDisplayName,
                ["remaining"] = Remaining(gift),
                ["expiresAt"] = expiresAt,
            };
            await emails.EnqueueAsync(templates.Render(ExpiringTemplate, model).To(email), ExpiringTemplate, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Qalığın mətni: məhsullarda qalan sətirlər, vauçerdə məbləğ.</summary>
    public static string Remaining(Gift gift)
    {
        ArgumentNullException.ThrowIfNull(gift);
        return gift.Kind == GiftKind.Items
            ? (gift.RemainingUnits == 0 ? "hamısı istifadə olunub" : string.Join(", ", gift.Items.Where(i => i.RemainingQty > 0).Select(i => $"{i.RemainingQty} × {i.Title}")))
            : Money.FromQepik(gift.RemainingQepik).ToString();
    }

    /// <summary>Məzmunun qısa təsviri: "2 × Kofe, 1 × Paxlava" və ya "50.00 AZN vauçer".</summary>
    public static string Contents(Gift gift)
    {
        ArgumentNullException.ThrowIfNull(gift);
        return gift.Kind == GiftKind.Items
            ? string.Join(", ", gift.Items.Select(i => $"{i.Qty} × {i.Title}"))
            : $"{Money.FromQepik(gift.AmountQepik)} {(gift.Scope == GiftScope.Network ? "şəbəkə vauçeri" : "vauçer")}";
    }

    /// <summary>Tema rənginin dizayn token yolu (<c>color.theme.justBecause</c>).</summary>
    public static string ThemeToken(GiftTheme theme)
    {
        var name = theme.ToString();
        return $"color.theme.{char.ToLowerInvariant(name[0])}{name[1..]}";
    }

    public static string BakuDate(DateTimeOffset instant) =>
        BakuTime.ToBakuDate(instant).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    public static IReadOnlyDictionary<string, string> Data(Gift gift)
    {
        ArgumentNullException.ThrowIfNull(gift);
        return new Dictionary<string, string> { ["type"] = "gift", ["giftId"] = gift.Id.Value.ToString() };
    }

    private async Task<string> WhereAsync(Gift gift, CancellationToken cancellationToken)
    {
        if (gift.BusinessId is not { } businessId)
        {
            return "QONAQET şəbəkəsindəki istənilən iştirakçı məkanda";
        }

        var business = await businesses.GetAsync(businessId, cancellationToken).ConfigureAwait(false);
        return business is null ? "QONAQET məkanında" : $"{business.Name}, {business.Address}";
    }
}
