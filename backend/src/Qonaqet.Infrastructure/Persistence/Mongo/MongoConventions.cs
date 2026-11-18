using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;

namespace Qonaqet.Infrastructure.Persistence.Mongo;

/// <summary>
/// Prosesdə bir dəfə qeydiyyatdan keçən BSON qaydaları: camelCase sahə adları (<c>isDeleted</c>,
/// <c>createdAt</c>), enum-lar sətir kimi, UTC tarixlər BSON <c>date</c> kimi, GUID standart UUID,
/// güclü tipli ID-lər Guid kimi. Naməlum sahələr oxunarkən atılmır (sxem təkamülü üçün).
/// </summary>
public static class MongoConventions
{
    private static int _registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) != 0)
        {
            return;
        }

        var pack = new ConventionPack
        {
            new CamelCaseElementNameConvention(),
            new EnumRepresentationConvention(BsonType.String),
            new IgnoreExtraElementsConvention(true),
        };
        ConventionRegistry.Register("qonaqet", pack, type => type.Namespace?.StartsWith("Qonaqet", StringComparison.Ordinal) == true);

        BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
        BsonSerializer.TryRegisterSerializer(new DateTimeOffsetSerializer(BsonType.DateTime));
        StronglyTypedIdSerializationProvider.Register();
    }
}
