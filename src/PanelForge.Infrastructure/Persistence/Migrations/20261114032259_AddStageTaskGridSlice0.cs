using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStageTaskGridSlice0 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "track",
                table: "pipeline_stages",
                type: "varchar(20)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "stage_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pipeline_stage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "varchar(30)", nullable: false),
                    is_input_outdated = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    outdated_reason = table.Column<string>(type: "varchar(500)", nullable: true),
                    reviewer_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    current_submission_id = table.Column<Guid>(type: "uuid", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stage_tasks", x => x.id);
                    table.ForeignKey(
                        name: "FK_stage_tasks_assignments_assignment_id",
                        column: x => x.assignment_id,
                        principalTable: "assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_stage_tasks_chapters_chapter_id",
                        column: x => x.chapter_id,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stage_tasks_pages_page_id",
                        column: x => x.page_id,
                        principalTable: "pages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stage_tasks_pipeline_stages_pipeline_stage_id",
                        column: x => x.pipeline_stage_id,
                        principalTable: "pipeline_stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stage_tasks_assignment_id",
                table: "stage_tasks",
                column: "assignment_id");

            migrationBuilder.CreateIndex(
                name: "ix_stage_tasks_page_id",
                table: "stage_tasks",
                column: "page_id");

            migrationBuilder.CreateIndex(
                name: "IX_stage_tasks_pipeline_stage_id",
                table: "stage_tasks",
                column: "pipeline_stage_id");

            migrationBuilder.CreateIndex(
                name: "ux_stage_tasks_chapter_stage",
                table: "stage_tasks",
                columns: new[] { "chapter_id", "pipeline_stage_id" },
                unique: true,
                filter: "page_id IS NULL AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ux_stage_tasks_chapter_stage_page",
                table: "stage_tasks",
                columns: new[] { "chapter_id", "pipeline_stage_id", "page_id" },
                unique: true,
                filter: "page_id IS NOT NULL AND is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stage_tasks");

            migrationBuilder.DropColumn(
                name: "track",
                table: "pipeline_stages");
        }
    }
}
