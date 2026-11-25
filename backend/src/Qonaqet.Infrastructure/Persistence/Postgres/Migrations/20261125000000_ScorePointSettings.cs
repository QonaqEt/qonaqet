using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Qonaqet.Infrastructure.Persistence.Postgres.Migrations;

/// <summary>Admin tərəfindən tənzimlənən xal dəyərləri (E11-T08): append-only versiya tarixçəsi.</summary>
[DbContext(typeof(QonaqetDbContext))]
[Migration("20261125000000_ScorePointSettings")]
public sealed class ScorePointSettingsTable : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        const string schema = QonaqetDbContext.Schema;

        migrationBuilder.CreateTable(
            name: "score_point_settings",
            schema: schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                gift_item_unit = table.Column<int>(type: "integer", nullable: false),
                gift_voucher_step = table.Column<int>(type: "integer", nullable: false),
                gift_voucher_step_qepik = table.Column<long>(type: "bigint", nullable: false),
                donor_claim = table.Column<int>(type: "integer", nullable: false),
                business_claim = table.Column<int>(type: "integer", nullable: false),
                corporate_gift = table.Column<int>(type: "integer", nullable: false),
                corporate_claim = table.Column<int>(type: "integer", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table => table.PrimaryKey("pk_score_point_settings", x => x.id));

        migrationBuilder.CreateIndex("ix_score_point_settings_created_at", "score_point_settings", "created_at", schema);
    }
}
