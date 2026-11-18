using System.Text.Json;
using System.Text.Json.Serialization;

namespace Qonaqet.SharedKernel;

/// <summary>Hər <see cref="IStronglyTypedId{TSelf}"/>-i JSON-da sadə GUID sətri kimi yazır/oxuyur.</summary>
public sealed class StronglyTypedIdJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => StronglyTypedId.IsStronglyTypedId(typeToConvert);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converterType = typeof(StronglyTypedIdJsonConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

/// <summary>Bir ID tipi üçün JSON çeviricisi.</summary>
public sealed class StronglyTypedIdJsonConverter<TId> : JsonConverter<TId>
    where TId : struct, IStronglyTypedId<TId>
{
    public override TId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || !reader.TryGetGuid(out var value) || value == Guid.Empty)
        {
            throw new JsonException($"{typeof(TId).Name} üçün etibarlı GUID gözlənilirdi.");
        }

        return TId.From(value);
    }

    public override void Write(Utf8JsonWriter writer, TId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.Value);
    }

    public override TId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (!Guid.TryParse(reader.GetString(), out var value) || value == Guid.Empty)
        {
            throw new JsonException($"{typeof(TId).Name} üçün etibarlı GUID açarı gözlənilirdi.");
        }

        return TId.From(value);
    }

    public override void WriteAsPropertyName(Utf8JsonWriter writer, TId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WritePropertyName(value.Value.ToString("D"));
    }
}
