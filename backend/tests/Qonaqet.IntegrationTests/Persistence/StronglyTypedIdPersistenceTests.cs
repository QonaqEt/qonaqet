using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Qonaqet.Infrastructure.Persistence.Mongo;
using Qonaqet.Infrastructure.Persistence.Postgres;
using Qonaqet.SharedKernel;

namespace Qonaqet.IntegrationTests.Persistence;

/// <summary>Güclü tipli ID-lərin Mongo (BSON) və EF Core çeviriciləri (E01-T03).</summary>
public sealed class StronglyTypedIdPersistenceTests
{
    [Fact]
    public void Mongo_stores_id_as_standard_uuid_and_reads_it_back()
    {
        StronglyTypedIdSerializationProvider.Register();
        StronglyTypedIdSerializationProvider.Register(); // təkrar qeydiyyat təhlükəsizdir

        var doc = new Doc { Id = StronglyTypedId.New<TestId>(), Other = StronglyTypedId.New<TestId>() };

        var bson = doc.ToBsonDocument();
        var back = BsonSerializer.Deserialize<Doc>(bson);

        var raw = bson["_id"].AsBsonBinaryData;
        Assert.Equal(BsonBinarySubType.UuidStandard, raw.SubType);
        Assert.Equal(doc.Id.Value, raw.ToGuid(GuidRepresentation.Standard));
        Assert.Equal(doc.Id, back.Id);
        Assert.Equal(doc.Other, back.Other);
    }

    [Fact]
    public void Ef_converter_maps_id_to_guid_and_back()
    {
        var converter = new StronglyTypedIdValueConverter<TestId>();
        var id = StronglyTypedId.New<TestId>();

        var provider = (Guid)converter.ConvertToProvider(id)!;
        var model = (TestId)converter.ConvertFromProvider(provider)!;

        Assert.Equal(id.Value, provider);
        Assert.Equal(id, model);
    }

    public readonly record struct TestId(Guid Value) : IStronglyTypedId<TestId>
    {
        public static TestId From(Guid value) => new(value);
    }

    public sealed class Doc
    {
        public TestId Id { get; set; }

        public TestId Other { get; set; }
    }
}
