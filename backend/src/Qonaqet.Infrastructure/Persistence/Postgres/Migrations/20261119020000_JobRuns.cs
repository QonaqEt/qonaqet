using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Qonaqet.Infrastructure.Persistence.Postgres.Migrations;

/// <summary>Scheduler icralarının tarixçəsi (E01-T11).</summary>
[DbContext(typeof(QonaqetDbContext))]
[Migration("20261119020000_JobRuns")]
public sealed class JobRuns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        const string schema = QonaqetDbContext.Schema;

        migrationBuilder.CreateTable(
            name: "job_runs",
            schema: schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                job_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                instance = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                items_processed = table.Column<int>(type: "integer", nullable: false),
                error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
            },
            constraints: table => table.PrimaryKey("pk_job_runs", x => x.id));

        migrationBuilder.CreateIndex("ix_job_runs_job_name_started_at", "job_runs", ["job_name", "started_at"], schema);
    }
}
