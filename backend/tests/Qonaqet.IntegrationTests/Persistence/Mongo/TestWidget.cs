using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Qonaqet.Application.Abstractions.Persistence;
using Qonaqet.Infrastructure.Messaging.Outbox;
using Qonaqet.Infrastructure.Persistence;
using Qonaqet.Infrastructure.Persistence.Mongo;
using Qonaqet.SharedKernel;

namespace Qonaqet.IntegrationTests.Persistence.Mongo;

public readonly record struct TestWidgetId(Guid Value) : IStronglyTypedId<TestWidgetId>
{
    public static TestWidgetId From(Guid value) => new(value);
}

/// <summary>Repository testləri üçün nümunə aqreqat (private setter-lər, parametrsiz konstruktor).</summary>
public sealed class TestWidget : AggregateRoot<TestWidgetId>
{
    public TestWidget(string name, DateTimeOffset at)
        : base(StronglyTypedId.New<TestWidgetId>(), at)
    {
        Name = name;
    }

    private TestWidget()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public void Rename(string name, DateTimeOffset at)
    {
        Name = name;
        MarkUpdated(at, null);
    }
}

public sealed class TestWidgetRepository(MongoContext context, IAggregateTracker tracker, IOutbox outbox, IClock clock)
    : SoftDeleteRepository<TestWidget, TestWidgetId>(context, tracker, outbox, clock, CollectionNameValue)
{
    public const string CollectionNameValue = "widgets";

    public Task<IReadOnlyList<TestWidget>> FindByNameAsync(string name, CancellationToken ct) =>
        FindAsync(Filter.Eq(x => x.Name, name), ct);
}

public sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

/// <summary>Bir test üçün repository + kontekst + outbox.</summary>
public sealed class MongoTestScope
{
    public MongoTestScope(MongoFixture fixture)
    {
        Database = fixture.NewDatabase();
        Context = new MongoContext(fixture.Client, Database);
        Clock = new FixedClock(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        Outbox = new MongoOutbox(Context);
        Repository = new TestWidgetRepository(Context, new AggregateTracker(), Outbox, Clock);
        UnitOfWork = new MongoUnitOfWork(Context);
    }

    public IMongoDatabase Database { get; }

    public MongoContext Context { get; }

    public FixedClock Clock { get; }

    public MongoOutbox Outbox { get; }

    public TestWidgetRepository Repository { get; }

    public MongoUnitOfWork UnitOfWork { get; }

    public static NullLogger<T> Logger<T>() => NullLogger<T>.Instance;

    /// <summary>Kolleksiyanı əvvəlcədən yaradır (tranzaksiya daxilində implicit yaratmanın yarışını aradan qaldırır).</summary>
    public async Task EnsureCollectionsAsync()
    {
        await Database.CreateCollectionAsync(TestWidgetRepository.CollectionNameValue);
        await Database.CreateCollectionAsync(MongoOutbox.CollectionName);
    }
}
