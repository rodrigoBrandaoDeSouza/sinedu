using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sinedu.Platform.Support.Migrations
{
    /// <inheritdoc />
    public partial class InitialSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "support");

            migrationBuilder.CreateTable(
                name: "access_requests",
                schema: "support",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_slug = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    tenant_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    requester_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    requester_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    duration_hours = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_requests", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_access_requests_requested_by_status",
                schema: "support",
                table: "access_requests",
                columns: new[] { "requested_by", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_access_requests_tenant_id_status",
                schema: "support",
                table: "access_requests",
                columns: new[] { "tenant_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_requests",
                schema: "support");
        }
    }
}
