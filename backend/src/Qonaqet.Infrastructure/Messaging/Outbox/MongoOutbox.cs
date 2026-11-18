using System.Text.Json;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Infrastructure.Persistence.Mongo;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Messaging.Outbox;

/// <summary>
/// <c>outboxMessages</c> sənədi (append-only). Relay (E01-T09) <see cref="ProcessedAt"/> boş olanları
/// RabbitMQ-ya ötürür; <see cref="Id"/> = hadisənin <c>EventId</c>-si (consumer idempotentliyi üçün).
/// </summary>
public sealed class OutboxMessage
{
    [BsonId]
    public Guid Id { get; set; }

    /// <summary>Hadisə tipinin tam adı.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>JSON (System.Text.Json, camelCase).</summary>
    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public int Attempts { get; set; }
}

/// <summary>Domen hadisələrini cari Mongo tranzaksiyasında <c>outboxMessages</c>-ə yazır.</summary>
public sealed class MongoOutbox(MongoContext context) : IOutbox
{
    public const string CollectionName = "outboxMessages";

    internal static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public async Task AddAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
        {
            return;
        }

        var messages = events.Select(ToMessage).ToArray();
        var collection = context.Collection<OutboxMessage>(CollectionName);
        if (context.Session is { } session)
        {
            await collection.InsertManyAsync(session, messages, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await collection.InsertManyAsync(messages, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    public static OutboxMessage ToMessage(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var type = domainEvent.GetType();
        return new OutboxMessage
        {
            Id = domainEvent.EventId,
            Type = type.FullName ?? type.Name,
            Payload = JsonSerializer.Serialize(domainEvent, type, JsonOptions),
            OccurredAt = domainEvent.OccurredAt,
        };
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new StronglyTypedIdJsonConverterFactory());
        return options;
    }
}
