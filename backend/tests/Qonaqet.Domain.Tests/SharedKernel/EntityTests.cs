using System.Text.Json;
using Qonaqet.SharedKernel;

namespace Qonaqet.Domain.Tests.SharedKernel;

public sealed class EntityTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.CreateVersion7();

    [Fact]
    public void New_entity_has_all_base_fields_and_is_not_deleted()
    {
        var entity = new SampleAggregate(StronglyTypedId.New<SampleId>(), T0, Actor);

        Assert.NotEqual(Guid.Empty, entity.Id.Value);
        Assert.Equal(T0, entity.CreatedAt);
        Assert.Equal(Actor, entity.CreatedBy);
        Assert.Equal(T0, entity.UpdatedAt);
        Assert.Null(entity.UpdatedBy);
        Assert.False(entity.IsDeleted);
        Assert.Null(entity.DeletedAt);
        Assert.Null(entity.DeletedBy);
        Assert.Null(entity.DeleteReason);
    }

    [Fact]
    public void Non_utc_or_empty_values_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new SampleAggregate(SampleId.From(Guid.Empty), T0));
        Assert.Throws<ArgumentException>(() => new SampleAggregate(StronglyTypedId.New<SampleId>(), T0.ToOffset(TimeSpan.FromHours(4))));
        Assert.Throws<ArgumentException>(() => StronglyTypedId.From<SampleId>(Guid.Empty));
    }

    [Fact]
    public void Soft_delete_and_restore_keep_an_audit_trail()
    {
        var entity = new SampleAggregate(StronglyTypedId.New<SampleId>(), T0);
        var t1 = T0.AddHours(1);
        var t2 = T0.AddHours(2);

        entity.SoftDelete(t1, Actor, "duplicate");
        entity.SoftDelete(t2, Guid.CreateVersion7(), "again");

        Assert.True(entity.IsDeleted);
        Assert.Equal(t1, entity.DeletedAt);
        Assert.Equal(Actor, entity.DeletedBy);
        Assert.Equal("duplicate", entity.DeleteReason);

        entity.Restore(t2, Actor);

        Assert.False(entity.IsDeleted);
        Assert.Null(entity.DeletedAt);
        Assert.Null(entity.DeleteReason);
        Assert.Equal(t2, entity.UpdatedAt);
        Assert.Equal(Actor, entity.UpdatedBy);
    }

    [Fact]
    public void Append_only_entities_cannot_be_deleted_or_restored()
    {
        var entry = new SampleAppendOnly(StronglyTypedId.New<SampleId>(), T0);

        Assert.True(entry.IsAppendOnly);
        Assert.Throws<InvalidOperationException>(() => entry.SoftDelete(T0, Actor, "x"));
        Assert.Throws<InvalidOperationException>(() => entry.Restore(T0, Actor));
        Assert.False(entry.IsDeleted);
    }

    [Fact]
    public void Entities_are_equal_by_type_and_id()
    {
        var id = StronglyTypedId.New<SampleId>();

        Assert.Equal(new SampleAggregate(id, T0), new SampleAggregate(id, T0.AddDays(1)));
        Assert.True(new SampleAggregate(id, T0) == new SampleAggregate(id, T0));
        Assert.NotEqual<Entity<SampleId>>(new SampleAggregate(id, T0), new SampleAppendOnly(id, T0));
    }

    [Fact]
    public void Aggregate_collects_and_dequeues_domain_events_in_order()
    {
        var aggregate = new SampleAggregate(StronglyTypedId.New<SampleId>(), T0);

        aggregate.DoSomething(T0);
        aggregate.DoSomething(T0.AddSeconds(1));

        Assert.Equal(2, aggregate.DomainEvents.Count);

        var events = aggregate.DequeueDomainEvents();

        Assert.Equal(2, events.Count);
        Assert.True(events[0].OccurredAt < events[1].OccurredAt);
        Assert.NotEqual(events[0].EventId, events[1].EventId);
        Assert.Empty(aggregate.DomainEvents);
        Assert.Empty(aggregate.DequeueDomainEvents());
    }

    [Fact]
    public void Domain_event_time_must_be_utc()
    {
        Assert.Throws<ArgumentException>(() => new SomethingHappened(T0.ToOffset(TimeSpan.FromHours(4))));
    }

    [Fact]
    public void Strongly_typed_id_round_trips_through_json_as_plain_guid()
    {
        var options = new JsonSerializerOptions { Converters = { new StronglyTypedIdJsonConverterFactory() } };
        var id = StronglyTypedId.New<SampleId>();
        var payload = new Payload(id, new Dictionary<SampleId, int> { [id] = 3 });

        var json = JsonSerializer.Serialize(payload, options);
        var back = JsonSerializer.Deserialize<Payload>(json, options)!;

        Assert.Contains($"\"Id\":\"{id.Value:D}\"", json, StringComparison.Ordinal);
        Assert.Equal(id, back.Id);
        Assert.Equal(3, back.Counts[id]);
    }

    [Theory]
    [InlineData("{\"Id\":\"not-a-guid\",\"Counts\":{}}")]
    [InlineData("{\"Id\":\"00000000-0000-0000-0000-000000000000\",\"Counts\":{}}")]
    [InlineData("{\"Id\":42,\"Counts\":{}}")]
    public void Invalid_json_id_is_rejected(string json)
    {
        var options = new JsonSerializerOptions { Converters = { new StronglyTypedIdJsonConverterFactory() } };

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Payload>(json, options));
    }

    [Fact]
    public void Strongly_typed_id_detection()
    {
        Assert.True(StronglyTypedId.IsStronglyTypedId(typeof(SampleId)));
        Assert.False(StronglyTypedId.IsStronglyTypedId(typeof(Guid)));
        Assert.False(StronglyTypedId.IsStronglyTypedId(typeof(SampleAggregate)));
    }

    public readonly record struct SampleId(Guid Value) : IStronglyTypedId<SampleId>
    {
        public static SampleId From(Guid value) => new(value);
    }

    public sealed record Payload(SampleId Id, Dictionary<SampleId, int> Counts);

    private sealed record SomethingHappened(DateTimeOffset At) : DomainEvent(At);

    private sealed class SampleAggregate(SampleId id, DateTimeOffset createdAt, Guid? createdBy = null)
        : AggregateRoot<SampleId>(id, createdAt, createdBy)
    {
        public void DoSomething(DateTimeOffset at) => Raise(new SomethingHappened(at));
    }

    private sealed class SampleAppendOnly(SampleId id, DateTimeOffset createdAt)
        : Entity<SampleId>(id, createdAt), IAppendOnly;
}
