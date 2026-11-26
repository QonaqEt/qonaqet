using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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
/// <item><see cref="GiftExpiryDonated"/>: müddəti bitən hədiyyənin qalığı hovuza <c>GiftExpiryDonation</c> (+).</item>
/// </summary>
public sealed class LedgerConsumer(IOptions<LedgerOptions> options) : IIntegrationEventConsumer
{
    public static readonly string ClaimRedeemedType = typeof(ClaimRedeemed).FullName!;

    public static readonly string PaymentRefundedType = typeof(PaymentRefunded).FullName!;

    public static readonly string PoolClaimReservedType = typeof(PoolClaimReserved).FullName!;

    public static readonly string PoolReservationReleasedType = typeof(PoolReservationReleased).FullName!;

    public static readonly string CampaignRolledOverType = typeof(CampaignRolledOver).FullName!;

    public static readonly string GiftRedeemedType = typeof(GiftRedeemed).FullName!;

    public static readonly string GiftExpiryDonatedType = typeof(GiftExpiryDonated).FullName!;

    public static readonly string GiftRedemptionReversedType = typeof(GiftRedemptionReversed).FullName!;

    public static readonly string PartnerGiftCreditedType = typeof(PartnerGiftCredited).FullName!;

    public static readonly string ClaimRedemptionReversedType = typeof(ClaimRedemptionReversed).FullName!;

    public string Name => "ledger";

    public IReadOnlyCollection<string> EventTypes { get; } = [ClaimRedeemedType, PaymentRefundedType, PoolClaimReservedType, PoolReservationReleasedType, CampaignRolledOverType, GiftRedeemedType, GiftExpiryDonatedType, GiftRedemptionReversedType, ClaimRedemptionReversedType, PartnerGiftCreditedType];

    public async Task HandleAsync(IntegrationMessage message, QonaqetDbContext context, CancellationToken cancellationToken)
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
        else if (message.Type == GiftRedemptionReversedType)
        {
            var reversed = Read<GiftRedemptionReversedPayload>(message);
            entries = await ReverseRedemptionAsync(context, "GiftRedemption", reversed.RedemptionId, message.MessageId, reversed.At, cancellationToken)
                .ConfigureAwait(false);
        }
        else if (message.Type == ClaimRedemptionReversedType)
        {
            var reversed = Read<ClaimRedemptionReversedPayload>(message);
            entries = reversed.Commissionable
                ? await ReverseRedemptionAsync(context, "Claim", reversed.ClaimId, message.MessageId, reversed.At, cancellationToken).ConfigureAwait(false)
                : [];
        }
        else if (message.Type == PartnerGiftCreditedType)
        {
            // Issuer hədiyyəsinin qalığı partnyor balansına qayıdır; hər səbəb üçün bir dəfə (mənbə hədiyyə + səbəb).
            var credited = Read<PartnerGiftCreditedPayload>(message);
            entries = [LedgerEntry.Post(
                LedgerAccount.PartnerClient(credited.PartnerClientId), LedgerEntryType.PartnerIssue, credited.AmountQepik, "Gift", credited.GiftId,
                credited.GiftId, $"partner-credit-{credited.Reason}", credited.At)];
        }
        else if (message.Type == GiftExpiryDonatedType)
        {
            // Mənbə hədiyyədir: bir hədiyyənin qalığı hovuza ən çox bir dəfə bağışlanır.
            var donated = Read<GiftExpiryDonatedPayload>(message);
            entries = [LedgerEntry.Post(
                LedgerAccount.Pool(donated.PoolId), LedgerEntryType.GiftExpiryDonation, donated.AmountQepik, "Gift", donated.GiftId, donated.GiftId,
                "gift-expiry-donation", donated.At)];
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
    }

    /// <summary>
    /// Geri alınan təhvilin tərs yazıları: təhvilin (<paramref name="refType"/>, <paramref name="refId"/>) <c>RedemptionCredit</c>/<c>Commission</c>
    /// yazılarının hər hesab üzrə <b>xalis</b> cəmi mənfi işarə ilə yazılır — claim təkrar təhvil/geri alma dövrlərində də düzgündür. Orijinal
    /// yazılar hələ yoxdursa (hadisələr gecikib) mesaj təkrar emal olunsun deyə xəta atılır.
    /// </summary>
    private static async Task<IReadOnlyList<LedgerEntry>> ReverseRedemptionAsync(
        QonaqetDbContext context, string refType, Guid refId, Guid sourceEventId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var net = await context.Set<LedgerEntry>().AsNoTracking()
            .Where(e => e.RefType == refType && e.RefId == refId && (e.Type == LedgerEntryType.RedemptionCredit || e.Type == LedgerEntryType.Commission))
            .GroupBy(e => new { e.AccountType, e.AccountId, e.Type })
            .Select(g => new { g.Key.AccountType, g.Key.AccountId, g.Key.Type, Amount = g.Sum(e => e.Amount) })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (!net.Exists(n => n.Type == LedgerEntryType.RedemptionCredit && n.Amount > 0))
        {
            throw new InvalidOperationException($"{refType} {refId}: geri alınacaq ledger yazısı hələ yoxdur.");
        }

        return [.. net.Where(n => n.Amount != 0).Select(n => LedgerEntry.Post(
            new LedgerAccount(n.AccountType, n.AccountId), n.Type, -n.Amount, refType, refId, sourceEventId, $"reverse:{n.AccountType}:{n.AccountId:N}:{n.Type}", at))];
    }

    private static T Read<T>(IntegrationMessage message) =>
        JsonSerializer.Deserialize<T>(message.Payload, OutboxSerializer.JsonOptions)
        ?? throw new InvalidOperationException($"Boş {message.Type} mesajı.");

    private sealed record GiftExpiryDonatedPayload(Guid GiftId, Guid PoolId, long AmountQepik, DateTimeOffset At);

    private sealed record PartnerGiftCreditedPayload(Guid GiftId, Guid PartnerClientId, long AmountQepik, GiftRefundReason Reason, DateTimeOffset At);

    private sealed record GiftRedemptionReversedPayload(Guid GiftId, Guid RedemptionId, DateTimeOffset At);

    private sealed record ClaimRedemptionReversedPayload(Guid ClaimId, bool Commissionable, DateTimeOffset At);

    private sealed record GiftRedeemedPayload(Guid GiftId, Guid RedemptionId, Guid BusinessId, long ValueQepik, DateTimeOffset At);

    private sealed record ClaimRedeemedPayload(Guid ClaimId, Guid BusinessId, long? AmountQepik, bool Commissionable, DateTimeOffset At);

    private sealed record RolloverPayload(Guid CampaignId, Guid FromPoolId, Guid ToPoolId, long AmountQepik, DateTimeOffset At);

    private sealed record PoolMovementPayload(Guid ClaimId, Guid PoolId, long AmountQepik, DateTimeOffset At);

    private sealed record PaymentRefundedPayload(Guid PaymentId, long Amount, DateTimeOffset At);
}
