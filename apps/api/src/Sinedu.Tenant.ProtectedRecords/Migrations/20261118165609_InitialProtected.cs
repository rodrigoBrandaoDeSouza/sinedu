using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sinedu.Tenant.ProtectedRecords.Migrations
{
    /// <inheritdoc />
    public partial class InitialProtected : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "protected");

            migrationBuilder.CreateTable(
                name: "exports",
                schema: "protected",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sections = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    justification_ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    justification_nonce = table.Column<byte[]>(type: "bytea", nullable: false),
                    justification_tag = table.Column<byte[]>(type: "bytea", nullable: false),
                    justification_wrapped_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exports", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "health_records",
                schema: "protected",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_version = table.Column<int>(type: "integer", nullable: false),
                    review_due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_health_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "protection_records",
                schema: "protected",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    valid_until = table.Column<DateOnly>(type: "date", nullable: true),
                    current_version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_protection_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "record_versions",
                schema: "protected",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    record_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    payload_schema_version = table.Column<int>(type: "integer", nullable: false),
                    ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    nonce = table.Column<byte[]>(type: "bytea", nullable: false),
                    tag = table.Column<byte[]>(type: "bytea", nullable: false),
                    wrapped_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_record_versions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "secure_attachments",
                schema: "protected",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    nonce = table.Column<byte[]>(type: "bytea", nullable: false),
                    tag = table.Column<byte[]>(type: "bytea", nullable: false),
                    wrapped_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    metadata_ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    metadata_nonce = table.Column<byte[]>(type: "bytea", nullable: false),
                    metadata_tag = table.Column<byte[]>(type: "bytea", nullable: false),
                    metadata_wrapped_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    ciphertext_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    removed_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_secure_attachments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_exports_student_id",
                schema: "protected",
                table: "exports",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_health_records_review_due_on",
                schema: "protected",
                table: "health_records",
                column: "review_due_on");

            migrationBuilder.CreateIndex(
                name: "ix_health_records_student_id",
                schema: "protected",
                table: "health_records",
                column: "student_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_protection_records_status_valid_until",
                schema: "protected",
                table: "protection_records",
                columns: new[] { "status", "valid_until" });

            migrationBuilder.CreateIndex(
                name: "ix_protection_records_student_id",
                schema: "protected",
                table: "protection_records",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_record_versions_record_kind_record_id_version",
                schema: "protected",
                table: "record_versions",
                columns: new[] { "record_kind", "record_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_record_versions_student_id",
                schema: "protected",
                table: "record_versions",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_secure_attachments_student_id_owner_kind_owner_id",
                schema: "protected",
                table: "secure_attachments",
                columns: new[] { "student_id", "owner_kind", "owner_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exports",
                schema: "protected");

            migrationBuilder.DropTable(
                name: "health_records",
                schema: "protected");

            migrationBuilder.DropTable(
                name: "protection_records",
                schema: "protected");

            migrationBuilder.DropTable(
                name: "record_versions",
                schema: "protected");

            migrationBuilder.DropTable(
                name: "secure_attachments",
                schema: "protected");
        }
    }
}
