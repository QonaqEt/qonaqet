using System.Text.Json;
using Microsoft.Extensions.Options;
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
/// </list>
/// Hədiyyə təhvili (<c>GiftRedeemed</c>) E09 ilə eyni <see cref="LedgerPostings.Redemption"/> qaydasına qoşulur.
/// </summary>
public sealed class LedgerConsumer(IOptions<LedgerOptions> options) : IIntegrationEventConsumer
{
    public static readonly string ClaimRedeemedType = typeof(ClaimRedeemed).FullName!;

    public static readonly string PaymentRefundedType = typeof(PaymentRefunded).FullName!;

    public string Name => "ledger";

    public IReadOnlyCollection<string> EventTypes { get; } = [ClaimRedeemedType, PaymentRefundedType];

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
        else if (message.Type == PaymentRefundedType)
        {
            var refund = Read<PaymentRefundedPayload>(message);
            entries = [LedgerPostings.Refund(refund.Amount, refund.PaymentId, message.MessageId, refund.At)];
        }

        context.Set<LedgerEntry>().AddRange(entries);
        return Task.CompletedTask;
    }

    private static T Read<T>(IntegrationMessage message) =>
        JsonSerializer.Deserialize<T>(message.Payload, OutboxSerializer.JsonOptions)
        ?? throw new InvalidOperationException($"Boş {message.Type} mesajı.");

    private sealed record ClaimRedeemedPayload(Guid ClaimId, Guid BusinessId, long? AmountQepik, bool Commissionable, DateTimeOffset At);

    private sealed record PaymentRefundedPayload(Guid PaymentId, long Amount, DateTimeOffset At);
}
