using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using Qonaqet.SharedKernel;

namespace Qonaqet.Infrastructure.Persistence.Mongo;

/// <summary>Güclü tipli ID-ni Mongo-da standart (subtype 4) UUID kimi saxlayır.</summary>
public sealed class StronglyTypedIdBsonSerializer<TId> : StructSerializerBase<TId>
    where TId : struct, IStronglyTypedId<TId>
{
    private static readonly GuidSerializer Inner = new(GuidRepresentation.Standard);

    public override TId Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args) =>
        TId.From(Inner.Deserialize(context, args));

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, TId value) =>
        Inner.Serialize(context, args, value.Value);
}

/// <summary>Bütün <see cref="IStronglyTypedId{TSelf}"/> tiplərinə avtomatik serializer verir.</summary>
public sealed class StronglyTypedIdSerializationProvider : IBsonSerializationProvider
{
    private static int _registered;

    public IBsonSerializer? GetSerializer(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!StronglyTypedId.IsStronglyTypedId(type))
        {
            return null;
        }

        var serializerType = typeof(StronglyTypedIdBsonSerializer<>).MakeGenericType(type);
        return (IBsonSerializer)Activator.CreateInstance(serializerType)!;
    }

    /// <summary>Provider-i prosesdə bir dəfə qeydiyyatdan keçirir (təkrar çağırış təhlükəsizdir).</summary>
    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 0)
        {
            BsonSerializer.RegisterSerializationProvider(new StronglyTypedIdSerializationProvider());
        }
    }
}
