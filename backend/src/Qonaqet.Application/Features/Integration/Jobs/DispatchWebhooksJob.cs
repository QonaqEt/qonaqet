using System.Globalization;
using Qonaqet.Application.Abstractions.Jobs;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Application.Features.Integration.Commands.PartnerWebhooks;
using Qonaqet.Domain.Integration;
using Qonaqet.SharedKernel;

namespace Qonaqet.Application.Features.Integration.Jobs;

/// <summary>
/// Webhook göndərişi (Worker, dəqiqədə bir): vaxtı çatan çatdırılmanı jurnala <c>Sending</c> sətri yazmaqla götürür ((delivery_id, seq) unikal
/// açarı ikinci instansı kəsir), imzalayıb göndərir və nəticəni yeni sətir kimi yazır — uğur, növbəti cəhd (1 d … 24 s) və ya <c>Failed</c>.
/// Başlıqlar: <c>X-Qonaqet-Event</c>, <c>X-Qonaqet-Delivery</c>, <c>X-Qonaqet-Timestamp</c>,
/// <c>X-Qonaqet-Signature: sha256=HMAC(secret, timestamp + "." + body)</c>.
/// </summary>
public sealed class DispatchWebhooksJob(IWebhookRepository webhooks, ISecretSealer sealer, IWebhookSender sender, IClock clock) : IScheduledJob
{
    public const string Name = "webhook-dispatch";

    public const int BatchSize = 50;

    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    /// <summary>Bu müddətdən çox <c>Sending</c>-də qalan (worker çöküb) çatdırılma yenidən götürülür.</summary>
    public static readonly TimeSpan SendingTimeout = TimeSpan.FromMinutes(5);

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var processed = 0;
        foreach (var due in await webhooks.DueAsync(clock.UtcNow, SendingTimeout, BatchSize, cancellationToken).ConfigureAwait(false))
        {
            var now = clock.UtcNow;
            var claimed = due.Claim(now);
            if (!await webhooks.TryAppendAsync(claimed, cancellationToken).ConfigureAwait(false))
            {
                continue; // başqa instans götürdü
            }

            var subscription = await webhooks.GetSubscriptionAsync(WebhookSubscriptionId.From(due.SubscriptionId), includeDeleted: true, cancellationToken)
                .ConfigureAwait(false);
            var secret = subscription is { IsDeleted: false } ? sealer.Unseal(WebhookMapping.SecretPurpose, subscription.SecretSealed) : null;
            WebhookDelivery outcome;
            if (subscription is null || subscription.IsDeleted || secret is null)
            {
                outcome = claimed.Abandon("subscription_unavailable", now);
            }
            else
            {
                var timestamp = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                var result = await sender.SendAsync(
                    new WebhookRequest(
                        new Uri(subscription.Url), claimed.EventType, claimed.DeliveryId, timestamp, WebhookDelivery.Signature(secret, timestamp, claimed.Payload),
                        claimed.Payload),
                    cancellationToken).ConfigureAwait(false);
                outcome = claimed.Complete(result.Succeeded, result.StatusCode, result.Body, clock.UtcNow);
            }

            await webhooks.TryAppendAsync(outcome, cancellationToken).ConfigureAwait(false);
            processed++;
        }

        return processed;
    }
}
