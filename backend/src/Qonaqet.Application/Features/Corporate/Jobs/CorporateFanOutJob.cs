using Microsoft.Extensions.Options;
using Qonaqet.Application.Abstractions.Jobs;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Gifts.Common;
using Qonaqet.Domain.Corporate;
using Qonaqet.Domain.Gifts;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Corporate.Jobs;

/// <summary>
/// Korporativ fan-out (Worker, dəqiqədə bir): ödənilmiş sifariş üçün hər alıcıya bir hədiyyə yaradır (<c>corporateOrderId</c>, göndərən adı =
/// şirkətin adı, tema <c>Corporate</c>, ödənilmiş — <c>Scheduled</c>). 200-lük paketlərlə işləyir:
/// <list type="bullet">
/// <item>hər hədiyyənin (<c>corporateOrderId</c>, <c>corporateRecipientIndex</c>) cütü partial unikal indeksdir — təkrar yazılan paket dublikat
/// yaratmır;</item>
/// <item>paketdən sonra sifarişin <c>sentCount</c>-u versiya kimi şərtlə irəliləyir; yarıda kəsilən iş növbəti keçiddə qalan alıcılardan
/// davam edir, iki instans eyni paketi iki dəfə irəlilədə bilmir.</item>
/// </list>
/// Status axını: <c>Paid</c> → <c>Sending</c> → <c>Completed</c>. Hədiyyələri çatdırılma job-u adi qaydada göndərir.
/// </summary>
public sealed class CorporateFanOutJob(
    ICorporateGiftOrderRepository orders,
    ICorporateRepository corporates,
    IGiftRepository gifts,
    IGiftLinkTokens links,
    IRedemptionCodes codes,
    IOptions<GiftOptions> options,
    IClock clock) : IScheduledJob
{
    public const string Name = "corporate-fanout";

    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    public const int BatchSize = 200;

    /// <summary>Bir keçiddə ən çox bu qədər sifariş.</summary>
    public const int OrdersPerRun = 10;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var created = 0;
        foreach (var order in await orders.ListToFanOutAsync(OrdersPerRun, cancellationToken).ConfigureAwait(false))
        {
            created += await FanOutAsync(order, cancellationToken).ConfigureAwait(false);
        }

        return created;
    }

    /// <summary>Sifarişin qalan alıcılarını paketlərlə emal edir; yaradılan hədiyyələrin sayı.</summary>
    public async Task<int> FanOutAsync(CorporateGiftOrder order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        var corporate = await corporates.GetAsync(order.CorporateId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Şirkət {order.CorporateId.Value} tapılmadı.");
        var expiry = new GiftExpiryPlan(order.ExpiryAction, order.ExpiryPoolId);
        var created = 0;
        while (order.SentCount < order.RecipientCount)
        {
            var now = clock.UtcNow;
            var start = order.SentCount;
            var end = Math.Min(start + BatchSize, order.RecipientCount);
            var batch = new List<Gift>(end - start);
            for (var index = start; index < end; index++)
            {
                var recipient = order.Recipients[index];
                var gift = Gift.CreateForCorporateOrder(
                    order.Content(), order.Id.Value, corporate.Name, order.PaymentId, new GiftRecipient(recipient.Name, recipient.Email), index,
                    order.Message, order.DeliverAt, options.Value.ValidityDays, expiry, links.Issue().Secret, now);
                gift.AttachShortCode(GiftCodes.ShortCodeHash(codes, gift.Id));
                batch.Add(gift);
            }

            created += await gifts.AddCorporateBatchAsync(batch, cancellationToken).ConfigureAwait(false);

            var expectedStatus = order.Status;
            order.AdvanceSent(end, now);
            if (!await orders.TryReplaceAsync(order, expectedStatus, start, cancellationToken).ConfigureAwait(false))
            {
                // Başqa instans bu sifarişi irəlilədir: onun işinə qarışmırıq (yaratdığımız paket dublikat deyil — indeks qoruyur).
                break;
            }
        }

        return created;
    }
}
