namespace Qonaqet.Application.Abstractions.Services;

/// <summary>Serverdə geri açıla bilən sirlər (webhook imza secret-i): AES-256-GCM, açar konfiqurasiyadan və məqsəddən törənir (ADR 0008).</summary>
public interface ISecretSealer
{
    string Seal(string purpose, string value);

    /// <summary>Açılmırsa (pozulub və ya açar dəyişib) <c>null</c>.</summary>
    string? Unseal(string purpose, string sealedValue);
}

/// <summary>Webhook-un HTTP göndərilməsi (imzalanmış başlıqlarla). Şəbəkə xətası uğursuz cəhd sayılır.</summary>
public interface IWebhookSender
{
    Task<WebhookSendResult> SendAsync(WebhookRequest request, CancellationToken cancellationToken);
}

public sealed record WebhookRequest(Uri Url, string EventType, Guid DeliveryId, string Timestamp, string Signature, string Body);

public sealed record WebhookSendResult(bool Succeeded, int? StatusCode, string? Body);
