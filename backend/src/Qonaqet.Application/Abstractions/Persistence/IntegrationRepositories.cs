using Qonaqet.Application.Common;
using Qonaqet.Domain.Integration;

namespace Qonaqet.Application.Abstractions.Persistence;

/// <summary><c>integration_clients</c> (Postgres). Ləğv olunmuş (soft delete) client-lər qlobal filtrlə görünmür.</summary>
public interface IIntegrationClientRepository
{
    void Add(IntegrationClient client);

    Task<IntegrationClient?> GetAsync(IntegrationClientId id, CancellationToken cancellationToken);

    Task<IntegrationClient?> FindByClientIdAsync(string clientId, CancellationToken cancellationToken);

    /// <summary>Client-lər (sahibə görə süzülə bilər), ən yenilər əvvəl.</summary>
    Task<IReadOnlyList<IntegrationClient>> ListAsync(IntegrationOwnerType? ownerType, Guid? ownerId, int limit, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Issuer partnyorun ön ödənişli balansı (ledger-də <c>PartnerClient</c> hesabı; balans = yazıların cəmi).
/// </summary>
public interface IPartnerFunding
{
    /// <summary>
    /// Öz Postgres tranzaksiyasında: hesab kilidlənir (advisory lock — paralel buraxılış balansı mənfiyə salmır), balans yoxlanılır, çatırsa
    /// <c>PartnerIssue</c> (−məbləğ) yazılır və <see cref="Domain.Integration.PartnerGiftFunded"/> outbox-a düşür. Balans çatmırsa <c>false</c>.
    /// Eyni hədiyyə ikinci dəfə silinmir (deterministik mənbə ID-si).
    /// </summary>
    Task<bool> TryFundGiftAsync(Guid clientId, Guid giftId, long amountQepik, DateTimeOffset at, CancellationToken cancellationToken);
}

/// <summary><c>webhook_subscriptions</c> və <c>webhook_deliveries</c> (status jurnalı, append-only).</summary>
public interface IWebhookRepository
{
    void AddSubscription(WebhookSubscription subscription);

    /// <summary>Silinməmiş abunə; silinibsə <c>null</c> (köhnə çatdırılmalar bağlanır).</summary>
    Task<WebhookSubscription?> GetSubscriptionAsync(WebhookSubscriptionId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookSubscription>> ListSubscriptionsAsync(Guid clientId, CancellationToken cancellationToken);

    /// <summary>Jurnala sətir yazır (dərhal); eyni (delivery_id, seq) artıq varsa — başqa worker qabaqlayıb — <c>false</c>.</summary>
    Task<bool> TryAppendAsync(WebhookDelivery row, CancellationToken cancellationToken);

    /// <summary>Göndərilməli çatdırılmalar: son sətri <c>Pending</c>/<c>Retrying</c> və vaxtı çatıb, və ya <c>Sending</c>-də ilişib qalıb.</summary>
    Task<IReadOnlyList<WebhookDelivery>> DueAsync(DateTimeOffset now, TimeSpan sendingTimeout, int limit, CancellationToken cancellationToken);

    Task<WebhookDelivery?> LatestAsync(Guid deliveryId, CancellationToken cancellationToken);

    /// <summary>Client-in çatdırılmalarının cari vəziyyəti, ən yenilər əvvəl.</summary>
    Task<PagedResult<WebhookDelivery>> ListLatestForClientAsync(Guid clientId, PageRequest page, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
