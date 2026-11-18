using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sinedu.SharedKernel.Audit.Migrations
{
    /// <inheritdoc />
    public partial class InitialAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "audit_events",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    student_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    resource_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    resource_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    result = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    is_support_session = table.Column<bool>(type: "boolean", nullable: false),
                    ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_occurred_at",
                schema: "audit",
                table: "audit_events",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_student_id_occurred_at",
                schema: "audit",
                table: "audit_events",
                columns: new[] { "student_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_user_id_occurred_at",
                schema: "audit",
                table: "audit_events",
                columns: new[] { "user_id", "occurred_at" });

            // Auditoria é somente inclusão: UPDATE, DELETE e TRUNCATE são bloqueados no próprio banco.
            migrationBuilder.Sql("""
                CREATE FUNCTION audit.prevent_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'audit.audit_events é somente inclusão';
                END $$;
                CREATE TRIGGER audit_events_append_only BEFORE UPDATE OR DELETE ON audit.audit_events
                    FOR EACH ROW EXECUTE FUNCTION audit.prevent_change();
                CREATE TRIGGER audit_events_no_truncate BEFORE TRUNCATE ON audit.audit_events
                    FOR EACH STATEMENT EXECUTE FUNCTION audit.prevent_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events",
                schema: "audit");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS audit.prevent_change();");
        }
    }
}
