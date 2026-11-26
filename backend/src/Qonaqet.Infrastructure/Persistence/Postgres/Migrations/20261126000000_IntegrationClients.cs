using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Qonaqet.Infrastructure.Persistence.Postgres.Migrations;

/// <summary>Partner API client-ləri (E13-T02): secret yalnız hash, rotasiyada əvvəlki secret 24 saat, soft delete = ləğv.</summary>
[DbContext(typeof(QonaqetDbContext))]
[Migration("20261126000000_IntegrationClients")]
public sealed class IntegrationClients : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        const string schema = QonaqetDbContext.Schema;

        migrationBuilder.CreateTable(
            name: "integration_clients",
            schema: schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                client_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                secret_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                previous_secret_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                previous_secret_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                owner_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                business_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                scopes = table.Column<List<string>>(type: "text[]", nullable: false),
                is_sandbox = table.Column<bool>(type: "boolean", nullable: false),
                rate_limit_per_minute = table.Column<int>(type: "integer", nullable: false),
                last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                delete_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
            },
            constraints: table => table.PrimaryKey("pk_integration_clients", x => x.id));

        migrationBuilder.CreateIndex(
            "ux_integration_clients_client_id", "integration_clients", "client_id", schema, unique: true, filter: "is_deleted = false");
        migrationBuilder.CreateIndex(
            "ix_integration_clients_owner", "integration_clients", ["owner_type", "owner_id"], schema, filter: "is_deleted = false");
    }
}
