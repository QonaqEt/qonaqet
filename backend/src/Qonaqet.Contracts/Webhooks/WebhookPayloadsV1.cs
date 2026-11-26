namespace Qonaqet.Contracts.Webhooks;

/// <summary>
/// Webhook body-si (v1): <c>{ id, type, apiVersion, createdAt, data }</c>. Versiyalanıb və yerində dəyişdirilmir — yeni sahə/semantika yeni
/// versiyada gəlir. Şəxsi məlumat yoxdur (partnyorun özünün göndərdiyi <c>externalReference</c> istisna).
/// </summary>
/// <param name="Id">Hadisənin ID-si (eyni hadisə təkrar çatdırılsa eynidir — qəbul edən dublikatı bununla süzür).</param>
public sealed record WebhookEnvelopeV1<T>(Guid Id, string Type, string ApiVersion, DateTimeOffset CreatedAt, T Data)
{
    public const string Version = "v1";
}

/// <summary><c>gift.created</c>, <c>gift.delivered</c>, <c>gift.redeemed</c>, <c>gift.expired</c> (issuer partnyorun hədiyyələri).</summary>
public sealed record GiftWebhookV1(
    Guid GiftId, string? ExternalReference, string Status, long AmountQepik, long RemainingQepik, long? RedeemedNowQepik, DateTimeOffset ExpiresAt);

/// <summary><c>redemption.created</c>, <c>redemption.reversed</c> (partnyorun bizneslərində təhvil).</summary>
/// <param name="Kind"><c>Gift</c> və ya <c>Claim</c>.</param>
public sealed record RedemptionWebhookV1(Guid RedemptionId, Guid BusinessId, string Kind, long ValueQepik);

/// <summary><c>payout.created</c>.</summary>
public sealed record PayoutWebhookV1(Guid PayoutId, Guid BusinessId, long AmountQepik);

/// <summary><c>catalog.synced</c>.</summary>
public sealed record CatalogSyncedWebhookV1(Guid BusinessId, int Created, int Updated, int Unchanged, int Pruned);

/// <summary><c>partner.balance.low</c>.</summary>
public sealed record PartnerBalanceLowWebhookV1(long BalanceQepik, long ThresholdQepik);
