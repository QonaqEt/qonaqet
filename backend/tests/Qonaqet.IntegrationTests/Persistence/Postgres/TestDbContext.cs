using Microsoft.EntityFrameworkCore;
using Qonaqet.Application.Abstractions.Services;
using Qonaqet.Infrastructure.Persistence.Postgres;
using Qonaqet.SharedKernel;

namespace Qonaqet.IntegrationTests.Persistence.Postgres;

public readonly record struct TestDocId(Guid Value) : IStronglyTypedId<TestDocId>
{
    public static TestDocId From(Guid value) => new(value);
}

public sealed record TestDocRenamed(TestDocId DocId, string Name, DateTimeOffset At) : DomainEvent(At);

/// <summary>Soft-deletable nümunə aqreqat.</summary>
public sealed class TestDoc : AggregateRoot<TestDocId>
{
    public TestDoc(string name, long amountQepik, DateTimeOffset at)
        : base(StronglyTypedId.New<TestDocId>(), at)
    {
        Name = name;
        AmountQepik = amountQepik;
    }

    private TestDoc()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public long AmountQepik { get; private set; }

    public string Code { get; private set; } = Guid.NewGuid().ToString("N");

    public void Rename(string name, DateTimeOffset at)
    {
        Name = name;
        Raise(new TestDocRenamed(Id, name, at));
    }
}

/// <summary>Append-only nümunə entity.</summary>
public sealed class TestEntry : IAppendOnly
{
    public Guid Id { get; set; }

    public long Amount { get; set; }
}

/// <summary>Real kontekst + test entity-ləri; konvensiyalar (snake_case, filtr, partial indeks) eyni tətbiq olunur.</summary>
public sealed class TestDbContext(DbContextOptions options) : QonaqetDbContext(options)
{
    public DbSet<TestDoc> Docs => Set<TestDoc>();

    public DbSet<TestEntry> Entries => Set<TestEntry>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.AddStronglyTypedIdConversions(typeof(TestDbContext).Assembly);
    }

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestDoc>(b =>
        {
            b.ToTable("test_docs");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(100);
            b.HasIndex(x => x.Code).IsUnique();
            b.Ignore(x => x.DomainEvents);
        });
        modelBuilder.Entity<TestEntry>(b => b.ToTable("test_entries"));
    }
}

public sealed class TestUser(Guid id) : ICurrentUser
{
    public bool IsAuthenticated => true;

    public Guid? ActorId { get; } = id;

    public IReadOnlySet<string> Roles { get; } = new HashSet<string>();

    public Guid? BusinessId => null;

    public Guid? CorporateId => null;
}
