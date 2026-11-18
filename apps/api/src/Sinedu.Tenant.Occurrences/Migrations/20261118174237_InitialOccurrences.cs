using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Sinedu.Tenant.Occurrences.Migrations
{
    /// <inheritdoc />
    public partial class InitialOccurrences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "occurrences");

            migrationBuilder.CreateTable(
                name: "attachments",
                schema: "occurrences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurrence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attachments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "categories",
                schema: "occurrences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "revisions",
                schema: "occurrences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurrence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_revisions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "severities",
                schema: "occurrences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_severities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "occurrences",
                schema: "occurrences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    classroom_id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_year_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    severity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    involved = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    actions_taken = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    referrals = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    registered_by = table.Column<Guid>(type: "uuid", nullable: true),
                    registered_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    concluded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_occurrences", x => x.id);
                    table.ForeignKey(
                        name: "fk_occurrences_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "occurrences",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_occurrences_severities_severity_id",
                        column: x => x.severity_id,
                        principalSchema: "occurrences",
                        principalTable: "severities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "occurrences",
                table: "categories",
                columns: new[] { "id", "is_active", "name", "sort_order" },
                values: new object[,]
                {
                    { new Guid("0193a000-0000-7000-8000-000000000001"), true, "Indisciplina", 1 },
                    { new Guid("0193a000-0000-7000-8000-000000000002"), true, "Conflito", 2 },
                    { new Guid("0193a000-0000-7000-8000-000000000003"), true, "Atraso recorrente", 3 },
                    { new Guid("0193a000-0000-7000-8000-000000000004"), true, "Desrespeito", 4 },
                    { new Guid("0193a000-0000-7000-8000-000000000005"), true, "Dano material", 5 },
                    { new Guid("0193a000-0000-7000-8000-000000000006"), true, "Outros", 6 }
                });

            migrationBuilder.InsertData(
                schema: "occurrences",
                table: "severities",
                columns: new[] { "id", "name", "sort_order" },
                values: new object[,]
                {
                    { new Guid("0193a000-0000-7000-8000-000000000101"), "Leve", 1 },
                    { new Guid("0193a000-0000-7000-8000-000000000102"), "Moderada", 2 },
                    { new Guid("0193a000-0000-7000-8000-000000000103"), "Grave", 3 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_occurrence_id",
                schema: "occurrences",
                table: "attachments",
                column: "occurrence_id");

            migrationBuilder.CreateIndex(
                name: "ix_categories_name",
                schema: "occurrences",
                table: "categories",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_occurrences_category_id",
                schema: "occurrences",
                table: "occurrences",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_occurrences_classroom_id_occurred_at",
                schema: "occurrences",
                table: "occurrences",
                columns: new[] { "classroom_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_occurrences_occurred_at",
                schema: "occurrences",
                table: "occurrences",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_occurrences_severity_id",
                schema: "occurrences",
                table: "occurrences",
                column: "severity_id");

            migrationBuilder.CreateIndex(
                name: "ix_occurrences_status",
                schema: "occurrences",
                table: "occurrences",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_occurrences_student_id_occurred_at",
                schema: "occurrences",
                table: "occurrences",
                columns: new[] { "student_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_revisions_occurrence_id_version",
                schema: "occurrences",
                table: "revisions",
                columns: new[] { "occurrence_id", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attachments",
                schema: "occurrences");

            migrationBuilder.DropTable(
                name: "occurrences",
                schema: "occurrences");

            migrationBuilder.DropTable(
                name: "revisions",
                schema: "occurrences");

            migrationBuilder.DropTable(
                name: "categories",
                schema: "occurrences");

            migrationBuilder.DropTable(
                name: "severities",
                schema: "occurrences");
        }
    }
}
