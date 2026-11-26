using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Qonaqet.Domain.Leaderboard;

namespace Qonaqet.Infrastructure.Persistence.Postgres.Configurations;

public sealed class ScoreEventConfiguration : IEntityTypeConfiguration<ScoreEvent>
{
    public void Configure(EntityTypeBuilder<ScoreEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("score_events");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.SubjectType).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.SourceType).HasMaxLength(32).IsRequired();

        // CLAUDE.md: score_events(subject_type, subject_id, source_type, source_id) unikal. Append-only → adi indekslər.
        builder.HasIndex(x => new { x.SubjectType, x.SubjectId, x.SourceType, x.SourceId }).IsUnique();
        builder.HasIndex(x => new { x.SubjectType, x.CreatedAt });

        // Anti-gaming (E11-T02): istifadəçinin bugünkü və cütün bu ayki xal cəmi.
        builder.HasIndex(x => new { x.SubjectType, x.SubjectId, x.CreatedAt });
    }
}
