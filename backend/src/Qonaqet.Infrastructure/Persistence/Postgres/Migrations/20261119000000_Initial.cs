using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Qonaqet.Infrastructure.Persistence.Postgres.Migrations;

/// <summary>
/// İlk sxem: infrastruktur cədvəlləri (audit, outbox, inbox, idempotency). Əl ilə yazılıb (ADR 0006);
/// <c>PostgresStoreTests</c> bunun EF modeli ilə sütun-sütun uyğunluğunu yoxlayır.
/// <see cref="Down"/> yoxdur: məlumat heç vaxt silinmir, geri dönüş yeni (irəli) migrasiya ilə edilir.
/// </summary>
[DbContext(typeof(QonaqetDbContext))]
[Migration("20261119000000_Initial")]
public sealed class Initial : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        const string schema = QonaqetDbContext.Schema;

        migrationBuilder.EnsureSchema(schema);

        migrationBuilder.CreateTable(
            name: "audit_logs",
            schema: schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                actor_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                entity_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                data = table.Column<string>(type: "jsonb", nullable: false),
                ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table => table.PrimaryKey("pk_audit_logs", x => x.id));

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                payload = table.Column<string>(type: "jsonb", nullable: false),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                attempts = table.Column<int>(type: "integer", nullable: false),
                last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
            },
            constraints: table => table.PrimaryKey("pk_outbox_messages", x => x.id));

        migrationBuilder.CreateTable(
            name: "inbox_messages",
            schema: schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                message_id = table.Column<Guid>(type: "uuid", nullable: false),
                consumer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table => table.PrimaryKey("pk_inbox_messages", x => x.id));

        migrationBuilder.CreateTable(
            name: "idempotency_records",
            schema: schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                client_id = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                request_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                response = table.Column<string>(type: "jsonb", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table => table.PrimaryKey("pk_idempotency_records", x => x.id));

        migrationBuilder.CreateIndex("ix_audit_logs_entity_type_entity_id_created_at", "audit_logs", ["entity_type", "entity_id", "created_at"], schema);
        migrationBuilder.CreateIndex("ix_audit_logs_actor_id_created_at", "audit_logs", ["actor_id", "created_at"], schema);
        migrationBuilder.CreateIndex("ix_outbox_messages_processed_at", "outbox_messages", "processed_at", schema, filter: "processed_at IS NULL");
        migrationBuilder.CreateIndex("ix_inbox_messages_message_id_consumer", "inbox_messages", ["message_id", "consumer"], schema, unique: true);
        migrationBuilder.CreateIndex("ix_idempotency_records_client_id_key", "idempotency_records", ["client_id", "key"], schema, unique: true);
    }
}
