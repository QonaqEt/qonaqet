using Qonaqet.Application.Abstractions.Jobs;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Gifts.Common;
using Qonaqet.Domain.Gifts;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Gifts.Jobs;

/// <summary>
/// Hədiyyələrin çatdırılması (Worker, dəqiqədə bir): <c>Scheduled</c> və <c>deliverAt ≤ now</c> hədiyyələri <c>Delivered</c> edir. Email hesaba
/// uyğundursa alıcı bağlanır. Hər hədiyyə versiya şərti ilə (Mongo tranzaksiyasında, <c>GiftDelivered</c> outbox hadisəsi ilə birlikdə)
/// götürülür; bildirişlər yalnız keçidi qazanan instansdan gedir — təkrar işə düşən job ikinci email göndərmir.
/// </summary>
public sealed class DeliverScheduledGiftsJob(
    IGiftRepository gifts,
    IUserRepository users,
    IGiftLinkTokens links,
    GiftNotifications notifications,
    GiftTransactions transactions,
    IClock clock) : IScheduledJob
{
    public const string Name = "gift-delivery";

    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    /// <summary>Bir keçiddə ən çox bu qədər hədiyyə (qalanları növbəti keçiddə).</summary>
    public const int BatchSize = 200;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var due = await gifts.ListDueForDeliveryAsync(clock.UtcNow, BatchSize, cancellationToken).ConfigureAwait(false);
        var delivered = 0;
        foreach (var gift in due)
        {
            if (await DeliverAsync(gift, cancellationToken).ConfigureAwait(false))
            {
                delivered++;
            }
        }

        return delivered;
    }

    private async Task<bool> DeliverAsync(Gift gift, CancellationToken cancellationToken)
    {
        // Açıq token yalnız yaddaşda: email-ə gedir, sonra aqreqat şifrəli nüsxəni (lazım deyilsə) silir.
        var token = links.Unseal(gift.LinkTokenSealed);
        Guid? recipientUserId = null;
        if (gift.RecipientEmail is { } email
            && await users.GetByEmailAsync(email, cancellationToken).ConfigureAwait(false) is { IsBlocked: false } user)
        {
            recipientUserId = user.Id.Value;
        }

        var version = gift.Version;
        gift.Deliver(recipientUserId, clock.UtcNow);
        if (!await transactions.SaveAsync(gift, version, cancellationToken).ConfigureAwait(false))
        {
            // Başqa instans artıq çatdırıb və ya göndərən elə indi ləğv etdi.
            return false;
        }

        await notifications.DeliveredAsync(gift, token, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
