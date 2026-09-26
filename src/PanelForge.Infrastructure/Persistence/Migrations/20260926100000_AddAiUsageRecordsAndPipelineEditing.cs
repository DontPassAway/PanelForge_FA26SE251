using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiUsageRecordsAndPipelineEditing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_stage_transitions_def_from_to",
                table: "stage_transitions");

            migrationBuilder.DropIndex(
                name: "ix_pipeline_stages_definition_slug",
                table: "pipeline_stages");

            migrationBuilder.DropIndex(
                name: "ix_pipeline_stages_definition_stage_order",
                table: "pipeline_stages");

            migrationBuilder.CreateTable(
                name: "ai_usage_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    feature = table.Column<string>(type: "varchar(100)", nullable: false),
                    provider = table.Column<string>(type: "varchar(100)", nullable: false),
                    model = table.Column<string>(type: "varchar(100)", nullable: true),
                    prompt_tokens = table.Column<long>(type: "bigint", nullable: false),
                    completion_tokens = table.Column<long>(type: "bigint", nullable: false),
                    total_tokens = table.Column<long>(type: "bigint", nullable: false),
                    cost_usd = table.Column<decimal>(type: "numeric(12,6)", nullable: true),
                    status = table.Column<string>(type: "varchar(30)", nullable: false),
                    error_message = table.Column<string>(type: "varchar(1000)", nullable: true),
                    duration_ms = table.Column<int>(type: "integer", nullable: true),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    created_by = table.Column<string>(type: "varchar(100)", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<string>(type: "varchar(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_usage_records", x => x.id);
                    table.ForeignKey(
                        name: "FK_ai_usage_records_studio_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "studio_workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stage_transitions_def_from_to",
                table: "stage_transitions",
                columns: new[] { "pipeline_definition_id", "from_stage_id", "to_stage_id" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_stages_definition_slug",
                table: "pipeline_stages",
                columns: new[] { "pipeline_definition_id", "slug" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_stages_definition_stage_order",
                table: "pipeline_stages",
                columns: new[] { "pipeline_definition_id", "stage_order" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_entity_name_entity_id",
                table: "audit_logs",
                columns: new[] { "entity_name", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_usage_records_user_id",
                table: "ai_usage_records",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_usage_records_workspace_occurred",
                table: "ai_usage_records",
                columns: new[] { "workspace_id", "occurred_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_usage_records");

            migrationBuilder.DropIndex(
                name: "ix_stage_transitions_def_from_to",
                table: "stage_transitions");

            migrationBuilder.DropIndex(
                name: "ix_pipeline_stages_definition_slug",
                table: "pipeline_stages");

            migrationBuilder.DropIndex(
                name: "ix_pipeline_stages_definition_stage_order",
                table: "pipeline_stages");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_entity_name_entity_id",
                table: "audit_logs");

            migrationBuilder.CreateIndex(
                name: "ix_stage_transitions_def_from_to",
                table: "stage_transitions",
                columns: new[] { "pipeline_definition_id", "from_stage_id", "to_stage_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_stages_definition_slug",
                table: "pipeline_stages",
                columns: new[] { "pipeline_definition_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_stages_definition_stage_order",
                table: "pipeline_stages",
                columns: new[] { "pipeline_definition_id", "stage_order" },
                unique: true);
        }
    }
}
