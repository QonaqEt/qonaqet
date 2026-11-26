using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Qonaqet.Infrastructure.Persistence.Postgres.Migrations;

/// <summary>Xal hadisələri (E11-T01): append-only, təhvil anında yazılır.</summary>
[DbContext(typeof(QonaqetDbContext))]
[Migration("20261124000000_ScoreEvents")]
public sealed class ScoreEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        const string schema = QonaqetDbContext.Schema;

        migrationBuilder.CreateTable(
            name: "score_events",
            schema: schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                subject_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                source_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                source_id = table.Column<Guid>(type: "uuid", nullable: false),
                points = table.Column<int>(type: "integer", nullable: false),
                treats = table.Column<int>(type: "integer", nullable: false),
                value = table.Column<long>(type: "bigint", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table => table.PrimaryKey("pk_score_events", x => x.id));

        migrationBuilder.CreateIndex(
            "ix_score_events_subject_type_subject_id_source_type_source_id", "score_events", ["subject_type", "subject_id", "source_type", "source_id"], schema,
            unique: true);
        migrationBuilder.CreateIndex("ix_score_events_subject_type_created_at", "score_events", ["subject_type", "created_at"], schema);
    }
}
