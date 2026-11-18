using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sinedu.Platform.Metrics.Migrations
{
    /// <inheritdoc />
    public partial class InitialMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "metrics");

            migrationBuilder.CreateTable(
                name: "tenant_snapshots",
                schema: "metrics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    collected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    active_students = table.Column<int>(type: "integer", nullable: false),
                    active_classrooms = table.Column<int>(type: "integer", nullable: false),
                    active_members = table.Column<int>(type: "integer", nullable: false),
                    occurrences_last30days = table.Column<int>(type: "integer", nullable: false),
                    emails_sent_this_month = table.Column<int>(type: "integer", nullable: false),
                    emails_failed_open = table.Column<int>(type: "integer", nullable: false),
                    common_storage_bytes = table.Column<long>(type: "bigint", nullable: false),
                    schema_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    error = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_snapshots", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_snapshots_tenant_id_collected_at",
                schema: "metrics",
                table: "tenant_snapshots",
                columns: new[] { "tenant_id", "collected_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_snapshots",
                schema: "metrics");
        }
    }
}
