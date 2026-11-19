using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sinedu.Tenant.Imports.Migrations
{
    /// <inheritdoc />
    public partial class MappingPresets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mapping_presets",
                schema: "imports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    signature = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    mapping_json = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mapping_presets", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mapping_presets_signature",
                schema: "imports",
                table: "mapping_presets",
                column: "signature",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mapping_presets",
                schema: "imports");
        }
    }
}
