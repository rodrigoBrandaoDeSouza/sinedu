using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sinedu.Tenant.Imports.Migrations
{
    /// <inheritdoc />
    public partial class InitialImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "imports");

            migrationBuilder.CreateTable(
                name: "batches",
                schema: "imports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    file_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_json = table.Column<string>(type: "jsonb", nullable: false),
                    sheet = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    header_row = table.Column<int>(type: "integer", nullable: true),
                    mapping_json = table.Column<string>(type: "jsonb", nullable: true),
                    totals_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    validated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    purge_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_batches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rows",
                schema: "imports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sheet = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    row_number = table.Column<int>(type: "integer", nullable: false),
                    cells = table.Column<string[]>(type: "text[]", nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target_student_id = table.Column<Guid>(type: "uuid", nullable: true),
                    candidates = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    changes = table.Column<string[]>(type: "text[]", nullable: false),
                    errors = table.Column<string[]>(type: "text[]", nullable: false),
                    warnings = table.Column<string[]>(type: "text[]", nullable: false),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rows", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_batches_created_at",
                schema: "imports",
                table: "batches",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_rows_batch_id_action",
                schema: "imports",
                table: "rows",
                columns: new[] { "batch_id", "action" });

            migrationBuilder.CreateIndex(
                name: "ix_rows_batch_id_sheet_row_number",
                schema: "imports",
                table: "rows",
                columns: new[] { "batch_id", "sheet", "row_number" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "batches",
                schema: "imports");

            migrationBuilder.DropTable(
                name: "rows",
                schema: "imports");
        }
    }
}
