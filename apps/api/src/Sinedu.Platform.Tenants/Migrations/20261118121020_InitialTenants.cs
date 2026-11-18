using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sinedu.Platform.Tenants.Migrations
{
    /// <inheritdoc />
    public partial class InitialTenants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tenants");

            migrationBuilder.CreateTable(
                name: "networks",
                schema: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    uf = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    municipality = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_networks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                schema: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    network_id = table.Column<Guid>(type: "uuid", nullable: true),
                    institutional_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    database_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    secret_path = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    transit_key_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    bucket_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    schema_version = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    first_director_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    first_director_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    suspended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenants_networks_network_id",
                        column: x => x.network_id,
                        principalSchema: "tenants",
                        principalTable: "networks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "provisioning_steps",
                schema: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provisioning_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_provisioning_steps_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenants",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_provisioning_steps_tenant_id_step",
                schema: "tenants",
                table: "provisioning_steps",
                columns: new[] { "tenant_id", "step" });

            migrationBuilder.CreateIndex(
                name: "ix_tenants_network_id",
                schema: "tenants",
                table: "tenants",
                column: "network_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenants_slug",
                schema: "tenants",
                table: "tenants",
                column: "slug",
                unique: true);

            // Registro de escolas lido pelo Tenant.Host (sem segredos: credenciais ficam no Vault).
            migrationBuilder.Sql("""
                CREATE VIEW tenants.v_registry AS
                SELECT id, slug, display_name, status, schema_version, database_name, secret_path, transit_key_name, bucket_name
                FROM tenants.tenants;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS tenants.v_registry;");

            migrationBuilder.DropTable(
                name: "provisioning_steps",
                schema: "tenants");

            migrationBuilder.DropTable(
                name: "tenants",
                schema: "tenants");

            migrationBuilder.DropTable(
                name: "networks",
                schema: "tenants");
        }
    }
}
