using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Qonaqet.Infrastructure.Persistence.Postgres.Migrations;

/// <summary>Nişanlar (E11-T05): dövr sonunda verilən <c>Week Top 3</c> və <c>Month Top 10</c>, append-only.</summary>
[DbContext(typeof(QonaqetDbContext))]
[Migration("20261124030000_BadgeAwards")]
public sealed class BadgeAwards : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        const string schema = QonaqetDbContext.Schema;

        migrationBuilder.CreateTable(
            name: "badge_awards",
            schema: schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                subject_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                badge = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                period_key = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                rank = table.Column<int>(type: "integer", nullable: false),
                points = table.Column<int>(type: "integer", nullable: false),
                awarded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table => table.PrimaryKey("pk_badge_awards", x => x.id));

        migrationBuilder.CreateIndex(
            "ux_badge_awards_subject_badge_period", "badge_awards", ["subject_type", "subject_id", "badge", "period_key"], schema, unique: true);
        migrationBuilder.CreateIndex("ix_badge_awards_badge_period", "badge_awards", ["subject_type", "badge", "period_key"], schema);
    }
}
