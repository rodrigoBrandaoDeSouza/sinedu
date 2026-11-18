using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sinedu.Tenant.Students.Migrations
{
    /// <inheritdoc />
    public partial class GuardianContactAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "contact_authorized_at",
                schema: "students",
                table: "student_guardians",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "contact_authorized_by",
                schema: "students",
                table: "student_guardians",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contact_basis",
                schema: "students",
                table: "student_guardians",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "contact_authorized_at",
                schema: "students",
                table: "student_guardians");

            migrationBuilder.DropColumn(
                name: "contact_authorized_by",
                schema: "students",
                table: "student_guardians");

            migrationBuilder.DropColumn(
                name: "contact_basis",
                schema: "students",
                table: "student_guardians");
        }
    }
}
