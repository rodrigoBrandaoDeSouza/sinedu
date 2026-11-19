using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sinedu.Platform.Tenants.Migrations
{
    /// <inheritdoc />
    public partial class TenantClosure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "closed_at",
                schema: "tenants",
                table: "tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "closing_started_at",
                schema: "tenants",
                table: "tenants",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "closed_at",
                schema: "tenants",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "closing_started_at",
                schema: "tenants",
                table: "tenants");
        }
    }
}
