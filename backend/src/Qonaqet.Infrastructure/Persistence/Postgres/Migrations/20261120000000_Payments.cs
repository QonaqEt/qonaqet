using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Qonaqet.Infrastructure.Persistence.Postgres.Migrations;

/// <summary>Ödənişlər və refundlar (E07-T03).</summary>
[DbContext(typeof(QonaqetDbContext))]
[Migration("20261120000000_Payments")]
public sealed class Payments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        const string schema = QonaqetDbContext.Schema;

        migrationBuilder.CreateTable(
            name: "payments",
            schema: schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                payer_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                corporate_id = table.Column<Guid>(type: "uuid", nullable: true),
                partner_client_id = table.Column<Guid>(type: "uuid", nullable: true),
                purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                amount = table.Column<long>(type: "bigint", nullable: false),
                provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                provider_order_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                payment_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                refunded_amount = table.Column<long>(type: "bigint", nullable: false),
                payload = table.Column<string>(type: "jsonb", nullable: false),
                paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                version = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                delete_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
            },
            constraints: table => table.PrimaryKey("pk_payments", x => x.id));

        migrationBuilder.CreateTable(
            name: "refunds",
            schema: schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                amount = table.Column<long>(type: "bigint", nullable: false),
                reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                provider_ref = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                failure_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                delete_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
            },
            constraints: table => table.PrimaryKey("pk_refunds", x => x.id));

        // payments append-only-dir → adi indekslər; refunds soft-deletable → partial.
        migrationBuilder.CreateIndex("ix_payments_provider_order_id", "payments", "provider_order_id", schema, unique: true);
        migrationBuilder.CreateIndex("ix_payments_status_created_at", "payments", ["status", "created_at"], schema);
        migrationBuilder.CreateIndex("ix_payments_payer_user_id_created_at", "payments", ["payer_user_id", "created_at"], schema);
        migrationBuilder.CreateIndex("ix_payments_purpose_reference_id", "payments", ["purpose", "reference_id"], schema);
        migrationBuilder.CreateIndex("ix_refunds_payment_id_created_at", "refunds", ["payment_id", "created_at"], schema, filter: ModelConventions.NotDeletedFilter);
    }
}
