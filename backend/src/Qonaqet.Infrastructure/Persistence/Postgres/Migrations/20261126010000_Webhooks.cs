using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Qonaqet.Infrastructure.Persistence.Postgres.Migrations;

/// <summary>Webhook abunələri və çatdırılma jurnalı (E13-T08).</summary>
[DbContext(typeof(QonaqetDbContext))]
[Migration("20261126010000_Webhooks")]
public sealed class Webhooks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        const string schema = QonaqetDbContext.Schema;

        migrationBuilder.CreateTable(
            name: "webhook_subscriptions",
            schema: schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                client_id = table.Column<Guid>(type: "uuid", nullable: false),
                url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                secret_sealed = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                event_types = table.Column<List<string>>(type: "text[]", nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                delete_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
            },
            constraints: table => table.PrimaryKey("pk_webhook_subscriptions", x => x.id));
        migrationBuilder.CreateIndex(
            "ix_webhook_subscriptions_client_id", "webhook_subscriptions", "client_id", schema, filter: "is_deleted = false");

        migrationBuilder.CreateTable(
            name: "webhook_deliveries",
            schema: schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                delivery_id = table.Column<Guid>(type: "uuid", nullable: false),
                seq = table.Column<int>(type: "integer", nullable: false),
                subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                client_id = table.Column<Guid>(type: "uuid", nullable: false),
                event_id = table.Column<Guid>(type: "uuid", nullable: false),
                event_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                payload = table.Column<string>(type: "jsonb", nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                attempt = table.Column<int>(type: "integer", nullable: false),
                next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                response_code = table.Column<int>(type: "integer", nullable: true),
                response_body_snippet = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table => table.PrimaryKey("pk_webhook_deliveries", x => x.id));
        migrationBuilder.CreateIndex("ux_webhook_deliveries_delivery_seq", "webhook_deliveries", ["delivery_id", "seq"], schema, unique: true);
        migrationBuilder.CreateIndex(
            "ix_webhook_deliveries_subscription_status_next", "webhook_deliveries", ["subscription_id", "status", "next_attempt_at"], schema);
        migrationBuilder.CreateIndex("ix_webhook_deliveries_status_next", "webhook_deliveries", ["status", "next_attempt_at"], schema);
        migrationBuilder.CreateIndex("ix_webhook_deliveries_client_created", "webhook_deliveries", ["client_id", "created_at"], schema);
    }
}
