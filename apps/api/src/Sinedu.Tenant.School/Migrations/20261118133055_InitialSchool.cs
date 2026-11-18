using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sinedu.Tenant.School.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchool : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "school");

            migrationBuilder.CreateTable(
                name: "grades",
                schema: "school",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    short_name = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    stage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_grades", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "school_profile",
                schema: "school",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    institutional_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_school_profile", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "school_years",
                schema: "school",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_school_years", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "staff_members",
                schema: "school",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    function = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    professional_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    professional_phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_members", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "academic_terms",
                schema: "school",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_year_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_academic_terms", x => x.id);
                    table.ForeignKey(
                        name: "fk_academic_terms_school_years_school_year_id",
                        column: x => x.school_year_id,
                        principalSchema: "school",
                        principalTable: "school_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "classrooms",
                schema: "school",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_year_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grade_id = table.Column<Guid>(type: "uuid", nullable: false),
                    section = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    shift = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    external_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_classrooms", x => x.id);
                    table.ForeignKey(
                        name: "fk_classrooms_grades_grade_id",
                        column: x => x.grade_id,
                        principalSchema: "school",
                        principalTable: "grades",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_classrooms_years_school_year_id",
                        column: x => x.school_year_id,
                        principalSchema: "school",
                        principalTable: "school_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "teacher_assignments",
                schema: "school",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    classroom_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teacher_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_teacher_assignments_classrooms_classroom_id",
                        column: x => x.classroom_id,
                        principalSchema: "school",
                        principalTable: "classrooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_teacher_assignments_staff_staff_member_id",
                        column: x => x.staff_member_id,
                        principalSchema: "school",
                        principalTable: "staff_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_academic_terms_school_year_id_number",
                schema: "school",
                table: "academic_terms",
                columns: new[] { "school_year_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_classrooms_grade_id",
                schema: "school",
                table: "classrooms",
                column: "grade_id");

            migrationBuilder.CreateIndex(
                name: "ix_classrooms_school_year_id_grade_id_section_shift",
                schema: "school",
                table: "classrooms",
                columns: new[] { "school_year_id", "grade_id", "section", "shift" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_grades_stage_short_name",
                schema: "school",
                table: "grades",
                columns: new[] { "stage", "short_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_school_years_year",
                schema: "school",
                table: "school_years",
                column: "year",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_staff_members_user_id",
                schema: "school",
                table: "staff_members",
                column: "user_id",
                unique: true,
                filter: "user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_teacher_assignments_classroom_id",
                schema: "school",
                table: "teacher_assignments",
                column: "classroom_id");

            migrationBuilder.CreateIndex(
                name: "ix_teacher_assignments_staff_member_id_classroom_id",
                schema: "school",
                table: "teacher_assignments",
                columns: new[] { "staff_member_id", "classroom_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "academic_terms",
                schema: "school");

            migrationBuilder.DropTable(
                name: "school_profile",
                schema: "school");

            migrationBuilder.DropTable(
                name: "teacher_assignments",
                schema: "school");

            migrationBuilder.DropTable(
                name: "classrooms",
                schema: "school");

            migrationBuilder.DropTable(
                name: "staff_members",
                schema: "school");

            migrationBuilder.DropTable(
                name: "grades",
                schema: "school");

            migrationBuilder.DropTable(
                name: "school_years",
                schema: "school");
        }
    }
}
