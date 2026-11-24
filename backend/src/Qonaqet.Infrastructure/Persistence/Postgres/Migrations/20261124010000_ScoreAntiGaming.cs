using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Qonaqet.Infrastructure.Persistence.Postgres.Migrations;

/// <summary>Anti-gaming (E11-T02): göndərən→alıcı cüt limiti üçün alıcı və günlük/aylıq xal cəmi üçün indeks.</summary>
[DbContext(typeof(QonaqetDbContext))]
[Migration("20261124010000_ScoreAntiGaming")]
public sealed class ScoreAntiGaming : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        const string schema = QonaqetDbContext.Schema;

        migrationBuilder.AddColumn<Guid>("recipient_user_id", "score_events", "uuid", nullable: true, schema: schema);
        migrationBuilder.CreateIndex(
            "ix_score_events_subject_type_subject_id_created_at", "score_events", ["subject_type", "subject_id", "created_at"], schema);
    }
}
