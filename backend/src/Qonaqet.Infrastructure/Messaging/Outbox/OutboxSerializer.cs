using System.Text.Json;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Messaging.Outbox;

/// <summary>Hər iki outbox (Mongo + Postgres) üçün eyni hadisə formatı: tip adı + camelCase JSON.</summary>
public static class OutboxSerializer
{
    public static JsonSerializerOptions JsonOptions { get; } = Create();

    public static string TypeName(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var type = domainEvent.GetType();
        return type.FullName ?? type.Name;
    }

    public static string Serialize(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), JsonOptions);
    }

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new StronglyTypedIdJsonConverterFactory());
        options.TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver();
        options.MakeReadOnly();
        return options;
    }
}
