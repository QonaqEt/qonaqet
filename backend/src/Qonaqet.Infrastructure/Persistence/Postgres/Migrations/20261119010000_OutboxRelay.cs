using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Qonaqet.Infrastructure.Persistence.Postgres.Migrations;

/// <summary>Outbox relay üçün backoff və icarə sütunları (E01-T09).</summary>
[DbContext(typeof(QonaqetDbContext))]
[Migration("20261119010000_OutboxRelay")]
public sealed class OutboxRelay : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.AddColumn<DateTimeOffset>("next_attempt_at", "outbox_messages", "timestamp with time zone", nullable: true, schema: QonaqetDbContext.Schema);
        migrationBuilder.AddColumn<DateTimeOffset>("locked_until", "outbox_messages", "timestamp with time zone", nullable: true, schema: QonaqetDbContext.Schema);
    }
}
