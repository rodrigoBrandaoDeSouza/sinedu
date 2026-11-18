using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Sinedu.Tenant.Notifications.Migrations
{
    /// <inheritdoc />
    public partial class InitialNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.CreateTable(
                name: "attempts",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_no = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    provider_message_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    error_sanitized = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    trigger = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attempts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurrence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guardian_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    recipient_address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rendered_subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    rendered_body = table.Column<string>(type: "character varying(6000)", maxLength: 6000, nullable: false),
                    authorized_by = table.Column<Guid>(type: "uuid", nullable: false),
                    authorized_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    authorized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    provider_message_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    last_error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "templates",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_templates", x => x.id);
                });

            migrationBuilder.InsertData(
                schema: "notifications",
                table: "templates",
                columns: new[] { "id", "body", "channel", "is_active", "name", "sort_order", "subject", "updated_at" },
                values: new object[,]
                {
                    { new Guid("0193a000-0000-7000-8000-000000000201"), "Olá, {responsavel}.\n\nA equipe pedagógica do(a) {escola} gostaria de conversar com você sobre {aluno}. Por favor, entre em contato com a escola{contato_escola} ou compareça à secretaria no horário de atendimento.\n\nEsta é uma mensagem automática. Não responda a este e-mail.\n\nAtenciosamente,\nEquipe pedagógica — {escola}", "Email", true, "Convite para conversar com a escola", 1, "{escola}: pedido de contato", null },
                    { new Guid("0193a000-0000-7000-8000-000000000202"), "Olá, {responsavel}.\n\nInformamos que, em {data}, a escola fez um registro envolvendo {aluno}. Para saber mais, entre em contato com a escola{contato_escola}.\n\nEsta é uma mensagem automática. Não responda a este e-mail.\n\nAtenciosamente,\nEquipe pedagógica — {escola}", "Email", true, "Aviso de registro escolar", 2, "{escola}: registro escolar de {aluno}", null }
                });

            migrationBuilder.CreateIndex(
                name: "ix_attempts_notification_id",
                schema: "notifications",
                table: "attempts",
                column: "notification_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_idempotency_key",
                schema: "notifications",
                table: "notifications",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_occurrence_id",
                schema: "notifications",
                table: "notifications",
                column: "occurrence_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_status_next_attempt_at",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_student_id",
                schema: "notifications",
                table: "notifications",
                column: "student_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attempts",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "templates",
                schema: "notifications");
        }
    }
}
