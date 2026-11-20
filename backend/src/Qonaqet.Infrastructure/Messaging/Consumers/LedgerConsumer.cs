using System.Text.Json;
using Microsoft.Extensions.Options;
using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Ledger;
using Qonaqet.Domain.Payments;
using Qonaqet.Domain.Social;
using Qonaqet.Infrastructure.Messaging.Inbox;
using Qonaqet.Infrastructure.Messaging.Outbox;
using Qonaqet.Infrastructure.Persistence.Postgres;

namespace Qonaqet.Infrastructure.Messaging.Consumers;

/// <summary>Ledger parametrləri (<c>Ledger</c> bölməsi).</summary>
public sealed class LedgerOptions
{
    public const string SectionName = "Ledger";

    /// <summary>Onlayn ödənişli təhvildən komissiya faizi (default 7).</summary>
    public decimal CommissionPercent { get; set; } = CommissionPolicy.Default.Percent;
}

/// <summary>
/// Ledger consumer-i: təhvil və refund hadisələrini <c>ledger_entries</c>-ə yazır, inbox qeydi ilə <b>eyni</b> Postgres tranzaksiyasında.
/// İkiqat qoruma: inbox (mesaj + consumer) və hər yazının deterministik unikal <c>source_event_id</c>-si.
/// <list type="bullet">
/// <item><see cref="ClaimRedeemed"/>: onlayn ödənişli (remote, hovuz) → <c>RedemptionCredit</c> + 7% <c>Commission</c>; <c>BusinessDaily</c>/<c>Suspended</c> → heç nə.</item>
/// <item><see cref="PaymentRefunded"/>: platforma hesabından <c>Refund</c>.</item>
/// <item><see cref="PoolClaimReserved"/> / <see cref="PoolReservationReleased"/>: hovuz hesabında <c>PoolReserve</c> (−) / <c>PoolRelease</c> (+).</item>
/// </list>
/// <item><see cref="GiftRedeemed"/>: hədiyyə həmişə onlayn ödənilib → təhvil verən biznesə <c>RedemptionCredit</c> + 7% <c>Commission</c>
/// (şəbəkə vauçerində kredit vauçerin alındığı yerə yox, təhvil verən biznesə düşür).</item>
/// </summary>
public sealed class LedgerConsumer(IOptions<LedgerOptions> options) : IIntegrationEventConsumer
{
    public static readonly string ClaimRedeemedType = typeof(ClaimRedeemed).FullName!;

    public static readonly string PaymentRefundedType = typeof(PaymentRefunded).FullName!;

    public static readonly string PoolClaimReservedType = typeof(PoolClaimReserved).FullName!;

    public static readonly string PoolReservationReleasedType = typeof(PoolReservationReleased).FullName!;

    public static readonly string CampaignRolledOverType = typeof(CampaignRolledOver).FullName!;

    public static readonly string GiftRedeemedType = typeof(GiftRedeemed).FullName!;

    public string Name => "ledger";

    public IReadOnlyCollection<string> EventTypes { get; } = [ClaimRedeemedType, PaymentRefundedType, PoolClaimReservedType, PoolReservationReleasedType, CampaignRolledOverType, GiftRedeemedType];

    public Task HandleAsync(IntegrationMessage message, QonaqetDbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);

        IEnumerable<LedgerEntry> entries = [];
        if (message.Type == ClaimRedeemedType)
        {
            var claim = Read<ClaimRedeemedPayload>(message);
            entries = LedgerPostings.Redemption(
                claim.BusinessId, claim.AmountQepik ?? 0, claim.Commissionable, "Claim", claim.ClaimId, message.MessageId,
                new CommissionPolicy(options.Value.CommissionPercent), claim.At);
        }
        else if (message.Type == GiftRedeemedType)
        {
            var gift = Read<GiftRedeemedPayload>(message);
            entries = LedgerPostings.Redemption(
                gift.BusinessId, gift.ValueQepik, commissionable: true, "GiftRedemption", gift.RedemptionId, message.MessageId,
                new CommissionPolicy(options.Value.CommissionPercent), gift.At);
        }
        else if (message.Type == PaymentRefundedType)
        {
            var refund = Read<PaymentRefundedPayload>(message);
            entries = [LedgerPostings.Refund(refund.Amount, refund.PaymentId, message.MessageId, refund.At)];
        }

        else if (message.Type == PoolClaimReservedType || message.Type == PoolReservationReleasedType)
        {
            var pool = Read<PoolMovementPayload>(message);
            var reserve = message.Type == PoolClaimReservedType;
            // Mənbə claim-dir (hadisə deyil): eyni claim üçün bir rezerv və ən çox bir buraxılma.
            entries = [LedgerEntry.Post(
                LedgerAccount.Pool(pool.PoolId), reserve ? LedgerEntryType.PoolReserve : LedgerEntryType.PoolRelease,
                reserve ? -pool.AmountQepik : pool.AmountQepik, "Claim", pool.ClaimId, pool.ClaimId, reserve ? "pool-reserve" : "pool-release", pool.At)];
        }

        else if (message.Type == CampaignRolledOverType)
        {
            var rollover = Read<RolloverPayload>(message);
            entries =
            [
                LedgerEntry.Post(LedgerAccount.Pool(rollover.FromPoolId), LedgerEntryType.CampaignRollover, -rollover.AmountQepik, "Campaign",
                    rollover.CampaignId, message.MessageId, "rollover-out", rollover.At),
                LedgerEntry.Post(LedgerAccount.Pool(rollover.ToPoolId), LedgerEntryType.CampaignRollover, rollover.AmountQepik, "Campaign",
                    rollover.CampaignId, message.MessageId, "rollover-in", rollover.At),
            ];
        }

        context.Set<LedgerEntry>().AddRange(entries);
        return Task.CompletedTask;
    }

    private static T Read<T>(IntegrationMessage message) =>
        JsonSerializer.Deserialize<T>(message.Payload, OutboxSerializer.JsonOptions)
        ?? throw new InvalidOperationException($"Boş {message.Type} mesajı.");

    private sealed record GiftRedeemedPayload(Guid GiftId, Guid RedemptionId, Guid BusinessId, long ValueQepik, DateTimeOffset At);

    private sealed record ClaimRedeemedPayload(Guid ClaimId, Guid BusinessId, long? AmountQepik, bool Commissionable, DateTimeOffset At);

    private sealed record RolloverPayload(Guid CampaignId, Guid FromPoolId, Guid ToPoolId, long AmountQepik, DateTimeOffset At);

    private sealed record PoolMovementPayload(Guid ClaimId, Guid PoolId, long AmountQepik, DateTimeOffset At);

    private sealed record PaymentRefundedPayload(Guid PaymentId, long Amount, DateTimeOffset At);
}
