using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Qonaqet.Infrastructure.Persistence.Postgres.Migrations;

/// <summary>Payout-lar və gecəlik uzlaşdırma (E07-T06).</summary>
[DbContext(typeof(QonaqetDbContext))]
[Migration("20261120020000_PayoutsReconciliation")]
public sealed class PayoutsReconciliation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        const string schema = QonaqetDbContext.Schema;

        migrationBuilder.CreateTable(
            name: "payouts",
            schema: schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                business_id = table.Column<Guid>(type: "uuid", nullable: false),
                amount = table.Column<long>(type: "bigint", nullable: false),
                bank_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                created_by_admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                delete_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
            },
            constraints: table => table.PrimaryKey("pk_payouts", x => x.id));

        migrationBuilder.CreateTable(
            name: "reconciliation_runs",
            schema: schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                accounts_checked = table.Column<int>(type: "integer", nullable: false),
                mismatches = table.Column<int>(type: "integer", nullable: false),
            },
            constraints: table => table.PrimaryKey("pk_reconciliation_runs", x => x.id));

        migrationBuilder.CreateTable(
            name: "reconciliation_mismatches",
            schema: schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                run_id = table.Column<Guid>(type: "uuid", nullable: false),
                account_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                account_id = table.Column<Guid>(type: "uuid", nullable: false),
                operational_balance = table.Column<long>(type: "bigint", nullable: false),
                ledger_balance = table.Column<long>(type: "bigint", nullable: false),
                difference = table.Column<long>(type: "bigint", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table => table.PrimaryKey("pk_reconciliation_mismatches", x => x.id));

        migrationBuilder.CreateIndex("ix_payouts_business_id_created_at", "payouts", ["business_id", "created_at"], schema, filter: ModelConventions.NotDeletedFilter);
        migrationBuilder.CreateIndex("ix_reconciliation_runs_created_at", "reconciliation_runs", "created_at", schema);
        migrationBuilder.CreateIndex("ix_reconciliation_mismatches_run_id", "reconciliation_mismatches", "run_id", schema);
    }
}
