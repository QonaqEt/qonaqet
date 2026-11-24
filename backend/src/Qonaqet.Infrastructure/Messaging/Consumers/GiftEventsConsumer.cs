using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Application.Features.Gifts.Common;
using Qonaqet.Domain.Gifts;
using Qonaqet.Infrastructure.Messaging.Inbox;
using Qonaqet.Infrastructure.Messaging.Outbox;
using Qonaqet.Infrastructure.Persistence.Postgres;

namespace Qonaqet.Infrastructure.Messaging.Consumers;

/// <summary>
/// Hədiyyə hadisələrinin bildiriş consumer-i ("gift-notifications"): <see cref="GiftRedeemed"/> → göndərənə və alıcıya "Hədiyyə istifadə edildi".
/// Ledger yazıları <see cref="LedgerConsumer"/>-dədir; xal <see cref="ScoreConsumer"/>-dədir, webhook-lar (E13) eyni hadisəyə öz consumer-ləri ilə qoşulur — hadisədə
/// lazım olan hər şey var (dəyər, vahid sayı, göndərən, alıcının hesabı, korporativ sifariş). Inbox eyni mesajın ikinci bildirişini kəsir.
/// </summary>
public sealed class GiftEventsConsumer(IServiceScopeFactory scopes) : IIntegrationEventConsumer
{
    public static readonly string GiftRedeemedType = typeof(GiftRedeemed).FullName!;

    public string Name => "gift-notifications";

    public IReadOnlyCollection<string> EventTypes { get; } = [GiftRedeemedType];

    public async Task HandleAsync(IntegrationMessage message, QonaqetDbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var payload = JsonSerializer.Deserialize<Payload>(message.Payload, OutboxSerializer.JsonOptions)
            ?? throw new InvalidOperationException($"Boş {message.Type} mesajı.");

        await using var scope = scopes.CreateAsyncScope();
        var gift = await scope.ServiceProvider.GetRequiredService<IGiftRepository>().GetAsync(GiftId.From(payload.GiftId), cancellationToken).ConfigureAwait(false);
        if (gift is null)
        {
            return;
        }

        await scope.ServiceProvider.GetRequiredService<GiftNotifications>().RedeemedAsync(gift, payload.ValueQepik, cancellationToken).ConfigureAwait(false);
    }

    private sealed record Payload(Guid GiftId, long ValueQepik);
}
