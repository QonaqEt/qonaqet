using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Qonaqet.Application.Features.Social.Events;
using Qonaqet.Domain.Businesses;
using Qonaqet.Domain.Users;
using Qonaqet.Infrastructure.Messaging.Inbox;
using Qonaqet.Infrastructure.Persistence.Postgres;

namespace Qonaqet.Infrastructure.Messaging.Consumers;

/// <summary>
/// Sosial qonaqlığın consumer-i: <see cref="UserBlocked"/> → istifadəçinin aktiv claim-ləri ləğv olunur; <see cref="BusinessDeleted"/> →
/// biznesin aktiv claim-ləri ləğv, təklifləri kaskad soft delete olunur; <see cref="BusinessRestored"/> → təkliflər bərpa olunur. İş Mongo-dadır və özü
/// idempotentdir (yalnız <c>Reserved</c> claim-lər atomik keçidlə); inbox qeydi isə eyni mesajın ikinci dəfə emalını Postgres-də kəsir.
/// </summary>
public sealed class SocialEventsConsumer(IServiceScopeFactory scopes) : IIntegrationEventConsumer
{
    public static readonly string UserBlockedType = typeof(UserBlocked).FullName!;

    public static readonly string BusinessDeletedType = typeof(BusinessDeleted).FullName!;

    public static readonly string BusinessRestoredType = typeof(BusinessRestored).FullName!;

    public string Name => "social-claims";

    public IReadOnlyCollection<string> EventTypes { get; } = [UserBlockedType, BusinessDeletedType, BusinessRestoredType];

    public async Task HandleAsync(IntegrationMessage message, QonaqetDbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        await using var scope = scopes.CreateAsyncScope();
        var cascade = scope.ServiceProvider.GetRequiredService<SocialCascade>();
        if (message.Type == UserBlockedType)
        {
            await cascade.OnUserBlockedAsync(GuidField(message, "userId"), cancellationToken).ConfigureAwait(false);
        }
        else if (message.Type == BusinessDeletedType)
        {
            await cascade.OnBusinessDeletedAsync(BusinessId.From(GuidField(message, "businessId")), cancellationToken).ConfigureAwait(false);
        }
        else if (message.Type == BusinessRestoredType)
        {
            await cascade.OnBusinessRestoredAsync(BusinessId.From(GuidField(message, "businessId")), cancellationToken).ConfigureAwait(false);
        }
    }

    internal static Guid GuidField(IntegrationMessage message, string name)
    {
        using var document = JsonDocument.Parse(message.Payload);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                // Güclü tipli ID ya xam GUID sətri, ya da { value } obyekti kimi gələ bilər.
                var value = property.Value.ValueKind == JsonValueKind.Object ? property.Value.EnumerateObject().First().Value : property.Value;
                return value.GetGuid();
            }
        }

        throw new InvalidOperationException($"{message.Type} mesajında {name} yoxdur.");
    }
}
