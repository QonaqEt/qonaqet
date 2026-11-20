using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Qonaqet.Domain.Ledger;

namespace Qonaqet.Infrastructure.Persistence.Postgres.Configurations;

public sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("ledger_entries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Ignore(x => x.Account);
        builder.Property(x => x.AccountType).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.RefType).HasMaxLength(64).IsRequired();

        // CLAUDE.md: ledger_entries(account_type, account_id, created_at); source_event_id unikal (idempotent consumer). Append-only → adi indekslər.
        builder.HasIndex(x => new { x.AccountType, x.AccountId, x.CreatedAt });
        builder.HasIndex(x => x.SourceEventId).IsUnique();
        builder.HasIndex(x => new { x.RefType, x.RefId });
    }
}
