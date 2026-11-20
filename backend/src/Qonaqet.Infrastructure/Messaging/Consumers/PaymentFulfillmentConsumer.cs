using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Application.Features.Donations.Events;
using Qonaqet.Domain.Ledger;
using Qonaqet.Domain.Payments;
using Qonaqet.Infrastructure.Messaging.Inbox;
using Qonaqet.Infrastructure.Messaging.Outbox;
using Qonaqet.Infrastructure.Persistence.Postgres;

namespace Qonaqet.Infrastructure.Messaging.Consumers;

/// <summary>
/// Ödəniş təsdiqləndikdən (və ya bağlandıqdan) sonra məqsədin yerinə yetirilməsi: Mongo işi <see cref="PaymentFulfillment"/>-dədir (idempotent),
/// onun qaytardığı ledger yazıları (məs. <c>PoolDeposit</c>) inbox qeydi ilə <b>eyni</b> Postgres tranzaksiyasında yazılır.
/// </summary>
public sealed class PaymentFulfillmentConsumer(IServiceScopeFactory scopes) : IIntegrationEventConsumer
{
    public static readonly string PaymentConfirmedType = typeof(PaymentConfirmed).FullName!;

    public static readonly string PaymentClosedUnpaidType = typeof(PaymentClosedUnpaid).FullName!;

    public string Name => "payment-fulfillment";

    public IReadOnlyCollection<string> EventTypes { get; } = [PaymentConfirmedType, PaymentClosedUnpaidType];

    public async Task HandleAsync(IntegrationMessage message, QonaqetDbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);
        var payload = JsonSerializer.Deserialize<Payload>(message.Payload, OutboxSerializer.JsonOptions)
            ?? throw new InvalidOperationException($"Boş {message.Type} mesajı.");
        var purpose = new PaidPurpose(payload.PaymentId, (PaymentPurpose)payload.Purpose, payload.ReferenceId, payload.Amount, payload.At);

        await using var scope = scopes.CreateAsyncScope();
        var fulfillment = scope.ServiceProvider.GetRequiredService<PaymentFulfillment>();
        if (message.Type == PaymentConfirmedType)
        {
            var entries = await fulfillment.OnPaidAsync(purpose, cancellationToken).ConfigureAwait(false);
            foreach (var entry in entries)
            {
                // Deterministik source ID: Mongo-dan sonra yarıda qalmış emalın təkrarında yazı artıq varsa, ikincisi yazılmır.
                if (!await context.Set<LedgerEntry>().AnyAsync(e => e.SourceEventId == entry.SourceEventId, cancellationToken).ConfigureAwait(false))
                {
                    context.Set<LedgerEntry>().Add(entry);
                }
            }
        }
        else if (message.Type == PaymentClosedUnpaidType)
        {
            await fulfillment.OnUnpaidAsync(purpose, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed record Payload(Guid PaymentId, int Purpose, Guid ReferenceId, long Amount, DateTimeOffset At);
}
