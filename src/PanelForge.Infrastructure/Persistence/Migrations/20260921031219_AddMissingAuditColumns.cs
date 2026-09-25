using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingAuditColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "workspace_members",
                type: "timestamptz",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "workspace_members",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "workspace_members",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "workspace_members",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "workspace_members",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "workspace_members",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "workspace_members",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "studio_workspaces",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "studio_workspaces",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "studio_workspaces",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "studio_workspaces",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "studio_workspaces",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "studio_workspaces",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "series",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "series",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "series",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "series",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "series",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "series",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "pipeline_definition_id",
                table: "series",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "script_lines",
                type: "timestamptz",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "script_lines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "script_lines",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "script_lines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "script_lines",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "script_lines",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "script_lines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "scenes",
                type: "timestamptz",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "scenes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "scenes",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "scenes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "scenes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "scenes",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "scenes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "panels",
                type: "timestamptz",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "panels",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "panels",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "panels",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "panels",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "panels",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "panels",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "pages",
                type: "timestamptz",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "pages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "pages",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "pages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "pages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "pages",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "pages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "current_stage_id",
                table: "pages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "external_preview_links",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "external_preview_links",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "external_preview_links",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "external_preview_links",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "external_preview_links",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "external_preview_links",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "elements",
                type: "timestamptz",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "elements",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "elements",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "elements",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "elements",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "elements",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "elements",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "chapters",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "chapters",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "chapters",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "chapters",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "chapters",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "chapters",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pipeline_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "varchar(255)", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_definitions", x => x.id);
                    table.ForeignKey(
                        name: "FK_pipeline_definitions_series_series_id",
                        column: x => x.series_id,
                        principalTable: "series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_pipeline_definitions_studio_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "studio_workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "series_bibles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "varchar(255)", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_series_bibles", x => x.id);
                    table.ForeignKey(
                        name: "FK_series_bibles_series_series_id",
                        column: x => x.series_id,
                        principalTable: "series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_series_bibles_studio_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "studio_workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_remembered_devices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "varchar(64)", nullable: false),
                    device_name = table.Column<string>(type: "varchar(255)", nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_remembered_devices", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_remembered_devices_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_stages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pipeline_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "varchar(100)", nullable: false),
                    slug = table.Column<string>(type: "varchar(50)", nullable: false),
                    stage_order = table.Column<int>(type: "integer", nullable: false),
                    color_code = table.Column<string>(type: "varchar(20)", nullable: true),
                    allowed_role = table.Column<string>(type: "varchar(30)", nullable: true),
                    is_approval_gate = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_initial = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_terminal = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    estimated_duration_days = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_stages", x => x.id);
                    table.ForeignKey(
                        name: "FK_pipeline_stages_pipeline_definitions_pipeline_definition_id",
                        column: x => x.pipeline_definition_id,
                        principalTable: "pipeline_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bible_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_bible_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "varchar(30)", nullable: false),
                    code = table.Column<string>(type: "varchar(50)", nullable: false),
                    name = table.Column<string>(type: "varchar(255)", nullable: false),
                    subtitle = table.Column<string>(type: "varchar(255)", nullable: true),
                    description = table.Column<string>(type: "text", nullable: false),
                    details_json = table.Column<string>(type: "text", nullable: false),
                    reference_image_url = table.Column<string>(type: "text", nullable: true),
                    priority = table.Column<string>(type: "varchar(20)", nullable: false),
                    strict_check = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bible_entries", x => x.id);
                    table.ForeignKey(
                        name: "FK_bible_entries_series_bibles_series_bible_id",
                        column: x => x.series_bible_id,
                        principalTable: "series_bibles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "varchar(20)", nullable: false),
                    target_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pipeline_stage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignee_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_role = table.Column<string>(type: "varchar(30)", nullable: false),
                    brief = table.Column<string>(type: "text", nullable: false),
                    reference_notes = table.Column<string>(type: "text", nullable: true),
                    reference_asset_urls_json = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    due_date = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    priority = table.Column<string>(type: "varchar(20)", nullable: false),
                    status = table.Column<string>(type: "varchar(30)", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    last_feedback_comment = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assignments", x => x.id);
                    table.ForeignKey(
                        name: "FK_assignments_chapters_chapter_id",
                        column: x => x.chapter_id,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_assignments_pipeline_stages_pipeline_stage_id",
                        column: x => x.pipeline_stage_id,
                        principalTable: "pipeline_stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_assignments_series_series_id",
                        column: x => x.series_id,
                        principalTable: "series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_assignments_users_assignee_user_id",
                        column: x => x.assignee_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stage_transitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pipeline_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_stage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_stage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transition_name = table.Column<string>(type: "varchar(150)", nullable: false),
                    required_role = table.Column<string>(type: "varchar(30)", nullable: true),
                    requires_comment = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_backward_transition = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stage_transitions", x => x.id);
                    table.ForeignKey(
                        name: "FK_stage_transitions_pipeline_definitions_pipeline_definition_~",
                        column: x => x.pipeline_definition_id,
                        principalTable: "pipeline_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_stage_transitions_pipeline_stages_from_stage_id",
                        column: x => x.from_stage_id,
                        principalTable: "pipeline_stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stage_transitions_pipeline_stages_to_stage_id",
                        column: x => x.to_stage_id,
                        principalTable: "pipeline_stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workflow_transition_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "varchar(20)", nullable: false),
                    from_stage_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_stage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    triggered_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comment = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_transition_logs", x => x.id);
                    table.ForeignKey(
                        name: "FK_workflow_transition_logs_pipeline_stages_from_stage_id",
                        column: x => x.from_stage_id,
                        principalTable: "pipeline_stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_workflow_transition_logs_pipeline_stages_to_stage_id",
                        column: x => x.to_stage_id,
                        principalTable: "pipeline_stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bible_entry_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bible_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    summary = table.Column<string>(type: "varchar(500)", nullable: false),
                    snapshot_json = table.Column<string>(type: "text", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: true),
                    content_hash = table.Column<string>(type: "varchar(128)", nullable: false),
                    associated_chapter_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bible_entry_revisions", x => x.id);
                    table.ForeignKey(
                        name: "FK_bible_entry_revisions_bible_entries_bible_entry_id",
                        column: x => x.bible_entry_id,
                        principalTable: "bible_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_script_lines_speaker_character_id",
                table: "script_lines",
                column: "speaker_character_id");

            migrationBuilder.CreateIndex(
                name: "IX_panels_current_stage_id",
                table: "panels",
                column: "current_stage_id");

            migrationBuilder.CreateIndex(
                name: "IX_pages_current_stage_id",
                table: "pages",
                column: "current_stage_id");

            migrationBuilder.CreateIndex(
                name: "IX_elements_speaker_character_id",
                table: "elements",
                column: "speaker_character_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignments_assignee_user_id",
                table: "assignments",
                column: "assignee_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignments_chapter_id",
                table: "assignments",
                column: "chapter_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignments_due_date",
                table: "assignments",
                column: "due_date");

            migrationBuilder.CreateIndex(
                name: "ix_assignments_pipeline_stage_id",
                table: "assignments",
                column: "pipeline_stage_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignments_series_id",
                table: "assignments",
                column: "series_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignments_status",
                table: "assignments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_assignments_target_entity_id",
                table: "assignments",
                column: "target_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_bible_entries_category",
                table: "bible_entries",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_bible_entries_series_bible_id",
                table: "bible_entries",
                column: "series_bible_id");

            migrationBuilder.CreateIndex(
                name: "ix_bible_entries_series_bible_id_code",
                table: "bible_entries",
                columns: new[] { "series_bible_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bible_entry_revisions_bible_entry_id",
                table: "bible_entry_revisions",
                column: "bible_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_bible_entry_revisions_bible_entry_id_version",
                table: "bible_entry_revisions",
                columns: new[] { "bible_entry_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bible_entry_revisions_content_hash",
                table: "bible_entry_revisions",
                column: "content_hash");

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_definitions_series_id",
                table: "pipeline_definitions",
                column: "series_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_definitions_workspace_id",
                table: "pipeline_definitions",
                column: "workspace_id");

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

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_stages_pipeline_definition_id",
                table: "pipeline_stages",
                column: "pipeline_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_series_bibles_series_id",
                table: "series_bibles",
                column: "series_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_series_bibles_workspace_id",
                table: "series_bibles",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "ix_stage_transitions_def_from_to",
                table: "stage_transitions",
                columns: new[] { "pipeline_definition_id", "from_stage_id", "to_stage_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stage_transitions_from_stage_id",
                table: "stage_transitions",
                column: "from_stage_id");

            migrationBuilder.CreateIndex(
                name: "ix_stage_transitions_pipeline_definition_id",
                table: "stage_transitions",
                column: "pipeline_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_stage_transitions_to_stage_id",
                table: "stage_transitions",
                column: "to_stage_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_remembered_devices_user_token",
                table: "user_remembered_devices",
                columns: new[] { "user_id", "token_hash" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_transition_logs_entity_id",
                table: "workflow_transition_logs",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_transition_logs_from_stage_id",
                table: "workflow_transition_logs",
                column: "from_stage_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_transition_logs_to_stage_id",
                table: "workflow_transition_logs",
                column: "to_stage_id");

            migrationBuilder.AddForeignKey(
                name: "FK_elements_bible_entries_speaker_character_id",
                table: "elements",
                column: "speaker_character_id",
                principalTable: "bible_entries",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_pages_pipeline_stages_current_stage_id",
                table: "pages",
                column: "current_stage_id",
                principalTable: "pipeline_stages",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_panels_pipeline_stages_current_stage_id",
                table: "panels",
                column: "current_stage_id",
                principalTable: "pipeline_stages",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_script_lines_bible_entries_speaker_character_id",
                table: "script_lines",
                column: "speaker_character_id",
                principalTable: "bible_entries",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_elements_bible_entries_speaker_character_id",
                table: "elements");

            migrationBuilder.DropForeignKey(
                name: "FK_pages_pipeline_stages_current_stage_id",
                table: "pages");

            migrationBuilder.DropForeignKey(
                name: "FK_panels_pipeline_stages_current_stage_id",
                table: "panels");

            migrationBuilder.DropForeignKey(
                name: "FK_script_lines_bible_entries_speaker_character_id",
                table: "script_lines");

            migrationBuilder.DropTable(
                name: "assignments");

            migrationBuilder.DropTable(
                name: "bible_entry_revisions");

            migrationBuilder.DropTable(
                name: "stage_transitions");

            migrationBuilder.DropTable(
                name: "user_remembered_devices");

            migrationBuilder.DropTable(
                name: "workflow_transition_logs");

            migrationBuilder.DropTable(
                name: "bible_entries");

            migrationBuilder.DropTable(
                name: "pipeline_stages");

            migrationBuilder.DropTable(
                name: "series_bibles");

            migrationBuilder.DropTable(
                name: "pipeline_definitions");

            migrationBuilder.DropIndex(
                name: "IX_script_lines_speaker_character_id",
                table: "script_lines");

            migrationBuilder.DropIndex(
                name: "IX_panels_current_stage_id",
                table: "panels");

            migrationBuilder.DropIndex(
                name: "IX_pages_current_stage_id",
                table: "pages");

            migrationBuilder.DropIndex(
                name: "IX_elements_speaker_character_id",
                table: "elements");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "workspace_members");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "workspace_members");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "workspace_members");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "workspace_members");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "workspace_members");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "workspace_members");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "workspace_members");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "studio_workspaces");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "studio_workspaces");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "studio_workspaces");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "studio_workspaces");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "studio_workspaces");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "studio_workspaces");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "series");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "series");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "series");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "series");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "series");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "series");

            migrationBuilder.DropColumn(
                name: "pipeline_definition_id",
                table: "series");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "script_lines");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "script_lines");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "script_lines");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "script_lines");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "script_lines");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "script_lines");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "script_lines");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "panels");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "panels");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "panels");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "panels");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "panels");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "panels");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "panels");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "current_stage_id",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "external_preview_links");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "external_preview_links");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "external_preview_links");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "external_preview_links");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "external_preview_links");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "external_preview_links");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "elements");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "elements");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "elements");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "elements");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "elements");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "elements");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "elements");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "chapters");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "chapters");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "chapters");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "chapters");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "chapters");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "chapters");
        }
    }
}
