using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Qonaqet.Infrastructure.Persistence.Postgres.Migrations;

/// <summary>Ledger (E07-T05): append-only, balans = <c>SUM(amount)</c>.</summary>
[DbContext(typeof(QonaqetDbContext))]
[Migration("20261120010000_Ledger")]
public sealed class Ledger : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        const string schema = QonaqetDbContext.Schema;

        migrationBuilder.CreateTable(
            name: "ledger_entries",
            schema: schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                account_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                account_id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                amount = table.Column<long>(type: "bigint", nullable: false),
                ref_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ref_id = table.Column<Guid>(type: "uuid", nullable: false),
                source_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table => table.PrimaryKey("pk_ledger_entries", x => x.id));

        migrationBuilder.CreateIndex("ix_ledger_entries_account_type_account_id_created_at", "ledger_entries", ["account_type", "account_id", "created_at"], schema);
        migrationBuilder.CreateIndex("ix_ledger_entries_source_event_id", "ledger_entries", "source_event_id", schema, unique: true);
        migrationBuilder.CreateIndex("ix_ledger_entries_ref_type_ref_id", "ledger_entries", ["ref_type", "ref_id"], schema);
    }
}
