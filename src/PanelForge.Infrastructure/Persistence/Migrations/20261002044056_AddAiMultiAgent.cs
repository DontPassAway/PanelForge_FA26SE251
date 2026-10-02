using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiMultiAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_workflow_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scene_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "varchar(30)", nullable: false),
                    provider = table.Column<string>(type: "varchar(50)", nullable: false),
                    model = table.Column<string>(type: "varchar(100)", nullable: false),
                    brief = table.Column<string>(type: "text", nullable: false),
                    reading_direction = table.Column<string>(type: "varchar(30)", nullable: false),
                    max_revision_rounds = table.Column<int>(type: "int", nullable: false),
                    revision_rounds = table.Column<int>(type: "int", nullable: false),
                    base_chapter_version = table.Column<long>(type: "bigint", nullable: false),
                    base_script_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    context_snapshot_json = table.Column<string>(type: "text", nullable: false),
                    bible_revision_ids_json = table.Column<string>(type: "text", nullable: false),
                    error_code = table.Column<string>(type: "varchar(100)", nullable: true),
                    error_message = table.Column<string>(type: "varchar(2000)", nullable: true),
                    last_activity_at_utc = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    created_by = table.Column<string>(type: "varchar(100)", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<string>(type: "varchar(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_workflow_runs", x => x.id);
                    table.ForeignKey(
                        name: "FK_ai_workflow_runs_chapters_chapter_id",
                        column: x => x.chapter_id,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ai_workflow_runs_scenes_scene_id",
                        column: x => x.scene_id,
                        principalTable: "scenes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ai_workflow_runs_series_series_id",
                        column: x => x.series_id,
                        principalTable: "series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ai_workflow_runs_studio_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "studio_workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "content_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scene_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "varchar(20)", nullable: false),
                    version_number = table.Column<int>(type: "int", nullable: false),
                    summary = table.Column<string>(type: "varchar(1000)", nullable: false),
                    payload_json = table.Column<string>(type: "text", nullable: false),
                    content_hash = table.Column<string>(type: "varchar(64)", nullable: false),
                    previous_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_suggestion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    mapping_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    created_by = table.Column<string>(type: "varchar(100)", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<string>(type: "varchar(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_revisions", x => x.id);
                    table.ForeignKey(
                        name: "FK_content_revisions_chapters_chapter_id",
                        column: x => x.chapter_id,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_content_revisions_scenes_scene_id",
                        column: x => x.scene_id,
                        principalTable: "scenes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ai_agent_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_index = table.Column<int>(type: "int", nullable: false),
                    agent = table.Column<string>(type: "varchar(30)", nullable: false),
                    round = table.Column<int>(type: "int", nullable: false),
                    parent_step_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "varchar(20)", nullable: false),
                    provider = table.Column<string>(type: "varchar(50)", nullable: false),
                    model = table.Column<string>(type: "varchar(100)", nullable: false),
                    prompt_version = table.Column<string>(type: "varchar(50)", nullable: false),
                    prompt_ref = table.Column<string>(type: "varchar(300)", nullable: false),
                    parameters_json = table.Column<string>(type: "text", nullable: false),
                    input_json = table.Column<string>(type: "text", nullable: false),
                    output_json = table.Column<string>(type: "text", nullable: true),
                    started_at_utc = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    prompt_tokens = table.Column<long>(type: "bigint", nullable: true),
                    completion_tokens = table.Column<long>(type: "bigint", nullable: true),
                    repair_attempts = table.Column<int>(type: "int", nullable: false),
                    usage_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    error_code = table.Column<string>(type: "varchar(100)", nullable: true),
                    error_message = table.Column<string>(type: "varchar(2000)", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    created_by = table.Column<string>(type: "varchar(100)", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<string>(type: "varchar(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_agent_steps", x => x.id);
                    table.ForeignKey(
                        name: "FK_ai_agent_steps_ai_usage_records_usage_record_id",
                        column: x => x.usage_record_id,
                        principalTable: "ai_usage_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ai_agent_steps_ai_workflow_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "ai_workflow_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ai_suggestions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scene_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "varchar(20)", nullable: false),
                    status = table.Column<string>(type: "varchar(30)", nullable: false),
                    payload_json = table.Column<string>(type: "text", nullable: false),
                    payload_hash = table.Column<string>(type: "varchar(64)", nullable: false),
                    summary = table.Column<string>(type: "varchar(1000)", nullable: false),
                    base_chapter_version = table.Column<long>(type: "bigint", nullable: false),
                    base_script_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    bible_revision_ids_json = table.Column<string>(type: "text", nullable: false),
                    parent_suggestion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    has_blocking_findings = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    created_by = table.Column<string>(type: "varchar(100)", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<string>(type: "varchar(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_suggestions", x => x.id);
                    table.ForeignKey(
                        name: "FK_ai_suggestions_ai_agent_steps_step_id",
                        column: x => x.step_id,
                        principalTable: "ai_agent_steps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ai_suggestions_ai_suggestions_parent_suggestion_id",
                        column: x => x.parent_suggestion_id,
                        principalTable: "ai_suggestions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ai_suggestions_ai_workflow_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "ai_workflow_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ai_suggestion_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    suggestion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision = table.Column<string>(type: "varchar(30)", nullable: false),
                    reason = table.Column<string>(type: "varchar(2000)", nullable: true),
                    decided_at_utc = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    chapter_version_at_decision = table.Column<long>(type: "bigint", nullable: false),
                    result_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modification_level = table.Column<string>(type: "varchar(30)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    created_by = table.Column<string>(type: "varchar(100)", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<string>(type: "varchar(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_suggestion_decisions", x => x.id);
                    table.ForeignKey(
                        name: "FK_ai_suggestion_decisions_ai_suggestions_suggestion_id",
                        column: x => x.suggestion_id,
                        principalTable: "ai_suggestions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ai_suggestion_decisions_content_revisions_result_revision_id",
                        column: x => x.result_revision_id,
                        principalTable: "content_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_agent_steps_usage_record_id",
                table: "ai_agent_steps",
                column: "usage_record_id");

            migrationBuilder.CreateIndex(
                name: "ux_ai_agent_steps_run_step",
                table: "ai_agent_steps",
                columns: new[] { "run_id", "step_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ai_suggestion_decisions_result_revision_id",
                table: "ai_suggestion_decisions",
                column: "result_revision_id");

            migrationBuilder.CreateIndex(
                name: "ux_ai_suggestion_decisions_accepted",
                table: "ai_suggestion_decisions",
                column: "suggestion_id",
                unique: true,
                filter: "decision = 'Accepted'");

            migrationBuilder.CreateIndex(
                name: "IX_ai_suggestions_parent_suggestion_id",
                table: "ai_suggestions",
                column: "parent_suggestion_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_suggestions_run",
                table: "ai_suggestions",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_suggestions_series_status",
                table: "ai_suggestions",
                columns: new[] { "series_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_ai_suggestions_step_id",
                table: "ai_suggestions",
                column: "step_id");

            migrationBuilder.CreateIndex(
                name: "IX_ai_workflow_runs_chapter_id",
                table: "ai_workflow_runs",
                column: "chapter_id");

            migrationBuilder.CreateIndex(
                name: "IX_ai_workflow_runs_scene_id",
                table: "ai_workflow_runs",
                column: "scene_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_workflow_runs_series_created",
                table: "ai_workflow_runs",
                columns: new[] { "series_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_workflow_runs_status_activity",
                table: "ai_workflow_runs",
                columns: new[] { "status", "last_activity_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_ai_workflow_runs_workspace_id",
                table: "ai_workflow_runs",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "IX_content_revisions_chapter_id",
                table: "content_revisions",
                column: "chapter_id");

            migrationBuilder.CreateIndex(
                name: "ux_content_revisions_scene_kind_version",
                table: "content_revisions",
                columns: new[] { "scene_id", "kind", "version_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_suggestion_decisions");

            migrationBuilder.DropTable(
                name: "ai_suggestions");

            migrationBuilder.DropTable(
                name: "content_revisions");

            migrationBuilder.DropTable(
                name: "ai_agent_steps");

            migrationBuilder.DropTable(
                name: "ai_workflow_runs");
        }
    }
}
