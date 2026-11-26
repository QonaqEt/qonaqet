using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Application.Features.Gifts.Events;
using Qonaqet.Domain.Gifts;
using Qonaqet.Domain.Integration;
using Qonaqet.Infrastructure.Messaging.Inbox;
using Qonaqet.Infrastructure.Messaging.Outbox;
using Qonaqet.Infrastructure.Persistence.Postgres;

namespace Qonaqet.Infrastructure.Messaging.Consumers;

/// <summary>
/// <see cref="PartnerGiftFunded"/>: balansdan ödənilmiş issuer hədiyyəsini <c>Scheduled</c> edir (link token-i ilə). API-nin sürətli yolu artıq
/// edibsə heç nə dəyişmir (versiya şərtli keçid) — ödəniş təsdiqi consumer-i ilə eyni zəmanət.
/// </summary>
public sealed class PartnerGiftConsumer(IServiceScopeFactory scopes) : IIntegrationEventConsumer
{
    public static readonly string PartnerGiftFundedType = typeof(PartnerGiftFunded).FullName!;

    public string Name => "partner-gifts";

    public IReadOnlyCollection<string> EventTypes { get; } = [PartnerGiftFundedType];

    public async Task HandleAsync(IntegrationMessage message, QonaqetDbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var funded = JsonSerializer.Deserialize<Payload>(message.Payload, OutboxSerializer.JsonOptions)
            ?? throw new InvalidOperationException($"Boş {message.Type} mesajı.");
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<GiftFulfillment>().OnPaidAsync(GiftId.From(funded.GiftId), funded.At, cancellationToken).ConfigureAwait(false);
    }

    private sealed record Payload(Guid GiftId, Guid ClientId, long AmountQepik, DateTimeOffset At);
}
