using System.Text;
using Qonaqet.Application.Abstractions.Services;

namespace Qonaqet.Infrastructure.Integration;

/// <summary>
/// Webhook-u partnyorun URL-inə POST edir (10 s timeout). 2xx uğurdur; qalan status və şəbəkə xətası uğursuz cəhddir. Cavab body-sinin yalnız
/// ilk hissəsi saxlanılır.
/// </summary>
public sealed class HttpWebhookSender(IHttpClientFactory clients) : IWebhookSender
{
    public const string ClientName = "webhooks";

    public async Task<WebhookSendResult> SendAsync(WebhookRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var message = new HttpRequestMessage(HttpMethod.Post, request.Url)
        {
            Content = new StringContent(request.Body, Encoding.UTF8, "application/json"),
        };
        message.Headers.Add("X-Qonaqet-Event", request.EventType);
        message.Headers.Add("X-Qonaqet-Delivery", request.DeliveryId.ToString("D"));
        message.Headers.Add("X-Qonaqet-Timestamp", request.Timestamp);
        message.Headers.Add("X-Qonaqet-Signature", request.Signature);
        try
        {
            using var response = await clients.CreateClient(ClientName).SendAsync(message, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new WebhookSendResult(response.IsSuccessStatusCode, (int)response.StatusCode, body.Length > 500 ? body[..500] : body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new WebhookSendResult(false, null, ex.GetType().Name);
        }
    }
}
