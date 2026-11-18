using Microsoft.EntityFrameworkCore;
using Qonaqet.Infrastructure.Persistence.Postgres.Configurations;
using Qonaqet.Infrastructure.Persistence.Postgres.Records;

namespace Qonaqet.Infrastructure.Persistence.Postgres;

/// <summary>
/// Maliyyə və append-only qeydlər üçün Postgres konteksti (<c>qonaqet</c> sxemi, snake_case).
/// Soft-deletable entity-lərə qlobal <c>!IsDeleted</c> filtri, append-only cədvəllərə silmə qadağası
/// interceptor-larla tətbiq olunur. Domen cədvəlləri (payments, ledger …) öz epic-ləri ilə gəlir.
/// </summary>
public class QonaqetDbContext(DbContextOptions options) : DbContext(options)
{
    public const string Schema = "qonaqet";

    public DbSet<AuditLogRecord> AuditLogs => Set<AuditLogRecord>();

    public DbSet<PostgresOutboxMessage> OutboxMessages => Set<PostgresOutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public DbSet<IdempotencyRecordRow> IdempotencyRecords => Set<IdempotencyRecordRow>();

    public DbSet<JobRunRecord> JobRuns => Set<JobRunRecord>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        configurationBuilder.AddStronglyTypedIdConversions(typeof(QonaqetDbContext).Assembly, typeof(Domain.DomainAssembly).Assembly);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(QonaqetDbContext).Assembly, t => t.Namespace == typeof(AuditLogConfiguration).Namespace);
        ConfigureModel(modelBuilder);
        modelBuilder.ApplySoftDeleteConventions();
        modelBuilder.ApplySnakeCaseNames();
    }

    /// <summary>Törəmə kontekstlər (testlər) əlavə entity-ləri burada qeyd edir; konvensiyalar sonra tətbiq olunur.</summary>
    protected virtual void ConfigureModel(ModelBuilder modelBuilder)
    {
    }
}
