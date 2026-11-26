using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Qonaqet.Infrastructure.Persistence.Postgres.Migrations;

/// <summary>Lövhə aqreqatları (E11-T03): qazanan × dövr üzrə xal cəmi, score consumer-i upsert edir.</summary>
[DbContext(typeof(QonaqetDbContext))]
[Migration("20261124020000_LeaderboardEntries")]
public sealed class LeaderboardEntries : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        const string schema = QonaqetDbContext.Schema;

        migrationBuilder.CreateTable(
            name: "leaderboard_entries",
            schema: schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                subject_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                period_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                period_key = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                points = table.Column<int>(type: "integer", nullable: false),
                treats = table.Column<int>(type: "integer", nullable: false),
                value = table.Column<long>(type: "bigint", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table => table.PrimaryKey("pk_leaderboard_entries", x => x.id));

        migrationBuilder.CreateIndex(
            "ux_leaderboard_entries_subject_period", "leaderboard_entries",
            ["subject_type", "subject_id", "period_type", "period_key"], schema, unique: true);
        migrationBuilder.CreateIndex(
            "ix_leaderboard_entries_board_points", "leaderboard_entries",
            ["subject_type", "period_type", "period_key", "points"], schema, descending: [false, false, false, true]);
    }
}
