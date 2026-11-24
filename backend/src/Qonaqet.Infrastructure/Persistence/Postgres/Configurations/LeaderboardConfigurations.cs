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

public sealed class LeaderboardEntryConfiguration : IEntityTypeConfiguration<LeaderboardEntry>
{
    public void Configure(EntityTypeBuilder<LeaderboardEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("leaderboard_entries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.SubjectType).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.PeriodType).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.PeriodKey).HasMaxLength(16).IsRequired();

        // CLAUDE.md: (subject_type, subject_id, period_type, period_key) unikal (upsert hədəfi) və (subject_type, period_type, period_key, points DESC).
        // Törəmə cədvəldir (score_events-dən yenidən qurulur), soft delete yoxdur → adi indekslər. Adlar açıqdır: avtomatik ad Postgres-in
        // 63 simvol limitini aşır.
        builder.HasIndex(x => new { x.SubjectType, x.SubjectId, x.PeriodType, x.PeriodKey }).IsUnique().HasDatabaseName("ux_leaderboard_entries_subject_period");
        builder.HasIndex(x => new { x.SubjectType, x.PeriodType, x.PeriodKey, x.Points })
            .IsDescending(false, false, false, true).HasDatabaseName("ix_leaderboard_entries_board_points");
    }
}
