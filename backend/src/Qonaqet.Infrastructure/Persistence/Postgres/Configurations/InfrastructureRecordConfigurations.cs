using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Qonaqet.Infrastructure.Persistence.Postgres.Records;

namespace Qonaqet.Infrastructure.Persistence.Postgres.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLogRecord>
{
    public void Configure(EntityTypeBuilder<AuditLogRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("audit_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ActorType).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(64).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Data).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Ip).HasMaxLength(64);
        builder.HasIndex(x => new { x.EntityType, x.EntityId, x.CreatedAt });
        builder.HasIndex(x => new { x.ActorId, x.CreatedAt });
    }
}

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<PostgresOutboxMessage>
{
    public void Configure(EntityTypeBuilder<PostgresOutboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("outbox_messages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Type).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(2000);

        // CLAUDE.md: outbox_messages(processed_at) WHERE processed_at IS NULL — relay-in növbəsi.
        builder.HasIndex(x => x.ProcessedAt).HasFilter("processed_at IS NULL");
    }
}

public sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("inbox_messages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Consumer).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => new { x.MessageId, x.Consumer }).IsUnique();
    }
}

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecordRow>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecordRow> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("idempotency_records");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ClientId).HasMaxLength(400).IsRequired();
        builder.Property(x => x.Key).HasMaxLength(200).IsRequired();
        builder.Property(x => x.RequestHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Response).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(x => new { x.ClientId, x.Key }).IsUnique();
    }
}

public sealed class JobRunConfiguration : IEntityTypeConfiguration<JobRunRecord>
{
    public void Configure(EntityTypeBuilder<JobRunRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("job_runs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.JobName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Instance).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Error).HasMaxLength(2000);
        builder.HasIndex(x => new { x.JobName, x.StartedAt });
    }
}
