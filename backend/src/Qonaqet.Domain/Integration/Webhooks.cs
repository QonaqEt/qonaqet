using System.Security.Cryptography;
using System.Text;
using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Integration;

public readonly record struct WebhookSubscriptionId(Guid Value) : IStronglyTypedId<WebhookSubscriptionId>
{
    public static WebhookSubscriptionId From(Guid value) => new(value);
}

/// <summary>Partnyorlara göndərilən webhook hadisələri (CLAUDE.md → Webhooks).</summary>
public static class WebhookEventTypes
{
    public const string GiftCreated = "gift.created";
    public const string GiftDelivered = "gift.delivered";
    public const string GiftRedeemed = "gift.redeemed";
    public const string GiftExpired = "gift.expired";
    public const string RedemptionCreated = "redemption.created";
    public const string RedemptionReversed = "redemption.reversed";
    public const string PayoutCreated = "payout.created";
    public const string CatalogSynced = "catalog.synced";
    public const string PartnerBalanceLow = "partner.balance.low";

    public static IReadOnlyList<string> All { get; } =
        [GiftCreated, GiftDelivered, GiftRedeemed, GiftExpired, RedemptionCreated, RedemptionReversed, PayoutCreated, CatalogSynced, PartnerBalanceLow];

    public static bool IsKnown(string type) => All.Contains(type, StringComparer.Ordinal);
}

/// <summary>
/// Partnyorun webhook abunəsi (Postgres <c>webhook_subscriptions</c>). İmza secret-i bir dəfə göstərilir; HMAC imzası üçün açıq dəyər lazım
/// olduğundan hash yox, server açarı ilə şifrələnmiş (sealed) nüsxə saxlanılır (ADR 0008). Silmə = soft delete.
/// </summary>
public sealed class WebhookSubscription : AggregateRoot<WebhookSubscriptionId>
{
    public const int UrlMaxLength = 500;

    public const int MaxPerClient = 10;

    private List<string> _eventTypes = [];

    private WebhookSubscription(WebhookSubscriptionId id, DateTimeOffset createdAt, Guid? createdBy)
        : base(id, createdAt, createdBy)
    {
    }

    private WebhookSubscription()
    {
    }

    public Guid ClientId { get; private set; }

    public string Url { get; private set; } = string.Empty;

    public string SecretSealed { get; private set; } = string.Empty;

    public IReadOnlyList<string> EventTypes
    {
        get => _eventTypes;
        private set => _eventTypes = [.. value];
    }

    public bool IsActive { get; private set; }

    public static WebhookSubscription Create(Guid clientId, Uri url, IReadOnlyCollection<string> eventTypes, string secretSealed, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(eventTypes);
        if (eventTypes.Count == 0 || !eventTypes.All(WebhookEventTypes.IsKnown))
        {
            throw new ArgumentException("Hadisə növləri düzgün deyil.", nameof(eventTypes));
        }

        return new WebhookSubscription(StronglyTypedId.New<WebhookSubscriptionId>(), Guard.Utc(at), clientId)
        {
            ClientId = Guard.NotEmpty(clientId),
            Url = url.AbsoluteUri,
            SecretSealed = Guard.NotNullOrWhiteSpace(secretSealed),
            _eventTypes = [.. eventTypes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            IsActive = true,
        };
    }

    public bool Wants(string eventType) => IsActive && _eventTypes.Contains(eventType, StringComparer.Ordinal);

    /// <summary>Partnyor URL-i: HTTPS (yalnız development-də HTTP), istifadəçi məlumatı və fraqment olmadan.</summary>
    public static bool IsValidUrl(Uri? url, bool allowHttp) =>
        url is { IsAbsoluteUri: true }
        && (url.Scheme == Uri.UriSchemeHttps || (allowHttp && url.Scheme == Uri.UriSchemeHttp))
        && string.IsNullOrEmpty(url.UserInfo) && string.IsNullOrEmpty(url.Fragment)
        && url.AbsoluteUri.Length <= UrlMaxLength;
}

public enum WebhookDeliveryStatus
{
    Pending,
    Sending,
    Succeeded,
    Retrying,
    Failed,
}

/// <summary>
/// Webhook çatdırılmasının status jurnalı (Postgres <c>webhook_deliveries</c>, append-only): hər vəziyyət dəyişikliyi yeni sətirdir
/// (<see cref="DeliveryId"/>, <see cref="Seq"/>) — (delivery_id, seq) unikal olduğundan bir çatdırılmanı eyni anda yalnız bir worker
/// götürə bilir. Cari vəziyyət ən böyük <see cref="Seq"/>-li sətirdir.
/// </summary>
public sealed class WebhookDelivery : IAppendOnly
{
    public const int SnippetMaxLength = 500;

    /// <summary>Uğursuz cəhddən sonrakı gözləmələr: 1 d, 5 d, 30 d, 2 s, 12 s, 24 s; bundan sonra <see cref="WebhookDeliveryStatus.Failed"/>.</summary>
    public static IReadOnlyList<TimeSpan> RetryDelays { get; } =
    [
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(12), TimeSpan.FromHours(24),
    ];

    private WebhookDelivery()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Çatdırılmanın sabit ID-si (<c>X-Qonaqet-Delivery</c>): hadisə + abunə üzrə deterministikdir.</summary>
    public Guid DeliveryId { get; private set; }

    public int Seq { get; private set; }

    public Guid SubscriptionId { get; private set; }

    public Guid ClientId { get; private set; }

    public Guid EventId { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    /// <summary>Göndərilən JSON body (versiyalanmış DTO).</summary>
    public string Payload { get; private set; } = "{}";

    public WebhookDeliveryStatus Status { get; private set; }

    /// <summary>Neçənci HTTP cəhdi (0 = hələ göndərilməyib).</summary>
    public int Attempt { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public int? ResponseCode { get; private set; }

    public string? ResponseBodySnippet { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Hadisə + abunə → sabit çatdırılma ID-si (consumer təkrar emal etsə də eyni ID).</summary>
    public static Guid DeliveryIdFor(Guid eventId, Guid subscriptionId, string eventType)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{eventId:N}:{subscriptionId:N}:{eventType}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    public static WebhookDelivery Pending(
        WebhookSubscription subscription, Guid eventId, string eventType, string payload, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        return new WebhookDelivery
        {
            Id = Guid.CreateVersion7(),
            DeliveryId = DeliveryIdFor(eventId, subscription.Id.Value, eventType),
            Seq = 1,
            SubscriptionId = subscription.Id.Value,
            ClientId = subscription.ClientId,
            EventId = eventId,
            EventType = eventType,
            Payload = payload,
            Status = WebhookDeliveryStatus.Pending,
            NextAttemptAt = Guard.Utc(at),
            CreatedAt = at,
        };
    }

    /// <summary>Worker çatdırılmanı götürür (növbəti sıra nömrəsi ilə — unikal açar ikinci worker-i kəsir).</summary>
    public WebhookDelivery Claim(DateTimeOffset at) => Next(WebhookDeliveryStatus.Sending, Attempt + 1, NextAttemptAt, null, null, at);

    /// <summary>HTTP cəhdinin nəticəsi: 2xx → uğur; əks halda növbəti gözləmə və ya son cəhddən sonra <c>Failed</c>.</summary>
    public WebhookDelivery Complete(bool succeeded, int? responseCode, string? responseBody, DateTimeOffset at)
    {
        var snippet = responseBody is { Length: > SnippetMaxLength } ? responseBody[..SnippetMaxLength] : responseBody;
        if (succeeded)
        {
            return Next(WebhookDeliveryStatus.Succeeded, Attempt, at, responseCode, snippet, at);
        }

        var retries = Attempt - 1;
        return retries < RetryDelays.Count
            ? Next(WebhookDeliveryStatus.Retrying, Attempt, at + RetryDelays[retries], responseCode, snippet, at)
            : Next(WebhookDeliveryStatus.Failed, Attempt, at, responseCode, snippet, at);
    }

    /// <summary>Göndərmək mümkün deyil (abunə silinib və ya secret açılmır): təkrar cəhd olmadan <c>Failed</c>.</summary>
    public WebhookDelivery Abandon(string reason, DateTimeOffset at) => Next(WebhookDeliveryStatus.Failed, Attempt, at, null, reason, at);

    /// <summary>Partnyorun əl ilə yenidən göndərmə istəyi: dərhal yeni cəhd.</summary>
    public WebhookDelivery Redeliver(DateTimeOffset at) => Next(WebhookDeliveryStatus.Pending, Attempt, at, ResponseCode, ResponseBodySnippet, at);

    public bool IsDue(DateTimeOffset now, TimeSpan sendingTimeout) =>
        (Status is WebhookDeliveryStatus.Pending or WebhookDeliveryStatus.Retrying && NextAttemptAt <= now)
        || (Status == WebhookDeliveryStatus.Sending && CreatedAt + sendingTimeout <= now);

    /// <summary><c>sha256=</c> + HMAC-SHA256(secret, timestamp + "." + body), kiçik hex — <c>X-Qonaqet-Signature</c>.</summary>
    public static string Signature(string secret, string timestamp, string body)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return "sha256=" + Convert.ToHexStringLower(mac);
    }

    private WebhookDelivery Next(
        WebhookDeliveryStatus status, int attempt, DateTimeOffset nextAttemptAt, int? responseCode, string? snippet, DateTimeOffset at) => new()
        {
            Id = Guid.CreateVersion7(),
            DeliveryId = DeliveryId,
            Seq = Seq + 1,
            SubscriptionId = SubscriptionId,
            ClientId = ClientId,
            EventId = EventId,
            EventType = EventType,
            Payload = Payload,
            Status = status,
            Attempt = attempt,
            NextAttemptAt = nextAttemptAt,
            ResponseCode = responseCode,
            ResponseBodySnippet = snippet,
            CreatedAt = Guard.Utc(at),
        };
}
