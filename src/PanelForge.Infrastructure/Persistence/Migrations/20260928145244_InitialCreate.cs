using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_email = table.Column<string>(type: "varchar(255)", nullable: true),
                    action = table.Column<string>(type: "varchar(100)", nullable: false),
                    entity_name = table.Column<string>(type: "varchar(100)", nullable: false),
                    entity_id = table.Column<string>(type: "varchar(100)", nullable: true),
                    changes_json = table.Column<string>(type: "text", nullable: true),
                    timestamp_utc = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    ip_address = table.Column<string>(type: "varchar(45)", nullable: true),
                    details = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "element_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "varchar(100)", nullable: false),
                    name = table.Column<string>(type: "varchar(200)", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    allowed_properties_json = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_element_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "export_presets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "varchar(100)", nullable: false),
                    name = table.Column<string>(type: "varchar(200)", nullable: false),
                    format_name = table.Column<string>(type: "varchar(100)", nullable: false),
                    config_options_json = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_export_presets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "varchar(100)", nullable: false),
                    name = table.Column<string>(type: "varchar(200)", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firebase_uid = table.Column<string>(type: "varchar(128)", nullable: true),
                    email = table.Column<string>(type: "varchar(320)", nullable: false),
                    password_hash = table.Column<string>(type: "varchar(255)", nullable: true),
                    full_name = table.Column<string>(type: "varchar(255)", nullable: false),
                    phone_number = table.Column<string>(type: "varchar(20)", nullable: true),
                    avatar_url = table.Column<string>(type: "varchar(2048)", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    role = table.Column<string>(type: "varchar(30)", nullable: false, defaultValue: "User"),
                    is_email_confirmed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    email_verification_token = table.Column<string>(type: "varchar(255)", nullable: true),
                    email_verification_token_expires_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    password_reset_token = table.Column<string>(type: "varchar(255)", nullable: true),
                    password_reset_token_expires_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    two_factor_secret = table.Column<string>(type: "varchar(255)", nullable: true),
                    can_create_studio = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_template_stages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pipeline_template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "varchar(100)", nullable: false),
                    name = table.Column<string>(type: "varchar(200)", nullable: false),
                    stage_order = table.Column<int>(type: "integer", nullable: false),
                    required_role = table.Column<string>(type: "varchar(30)", nullable: true),
                    gate_type = table.Column<string>(type: "varchar(50)", nullable: true),
                    is_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    configuration_json = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_template_stages", x => x.id);
                    table.ForeignKey(
                        name: "FK_pipeline_template_stages_pipeline_templates_pipeline_templa~",
                        column: x => x.pipeline_template_id,
                        principalTable: "pipeline_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "studio_workspaces",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "varchar(255)", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_quota_bytes = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    used_storage_bytes = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
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
                    table.PrimaryKey("PK_studio_workspaces", x => x.id);
                    table.ForeignKey(
                        name: "FK_studio_workspaces_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
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
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
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

            migrationBuilder.CreateTable(
                name: "series",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pipeline_definition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "varchar(500)", nullable: false),
                    synopsis = table.Column<string>(type: "text", nullable: true),
                    reading_direction = table.Column<string>(type: "varchar(20)", nullable: false),
                    genre = table.Column<string>(type: "varchar(100)", nullable: false, defaultValue: "Action"),
                    format = table.Column<string>(type: "varchar(50)", nullable: false, defaultValue: "Manga"),
                    release_schedule_json = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_series", x => x.id);
                    table.ForeignKey(
                        name: "FK_series_studio_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "studio_workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workspace_ai_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "varchar(100)", nullable: false),
                    api_key_encrypted = table.Column<string>(type: "text", nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    monthly_token_quota = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1000000L),
                    used_tokens_current_month = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    allowed_models_json = table.Column<string>(type: "text", nullable: false),
                    last_reset_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
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
                    table.PrimaryKey("PK_workspace_ai_configs", x => x.id);
                    table.ForeignKey(
                        name: "FK_workspace_ai_configs_studio_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "studio_workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workspace_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "varchar(30)", nullable: false),
                    joined_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
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
                    table.PrimaryKey("PK_workspace_members", x => x.id);
                    table.ForeignKey(
                        name: "FK_workspace_members_studio_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "studio_workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_workspace_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chapters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_number = table.Column<decimal>(type: "numeric(8,2)", nullable: false),
                    title = table.Column<string>(type: "varchar(500)", nullable: true),
                    target_release_date = table.Column<DateOnly>(type: "date", nullable: true),
                    version_vector = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    status = table.Column<string>(type: "varchar(20)", nullable: false, defaultValue: "InProduction"),
                    published_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
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
                    table.PrimaryKey("PK_chapters", x => x.id);
                    table.ForeignKey(
                        name: "FK_chapters_series_series_id",
                        column: x => x.series_id,
                        principalTable: "series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "consistency_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "varchar(200)", nullable: false),
                    rule_type = table.Column<string>(type: "varchar(100)", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    pattern = table.Column<string>(type: "text", nullable: true),
                    severity = table.Column<string>(type: "varchar(30)", nullable: false, defaultValue: "Warning"),
                    configuration_json = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
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
                    table.PrimaryKey("PK_consistency_rules", x => x.id);
                    table.ForeignKey(
                        name: "FK_consistency_rules_series_series_id",
                        column: x => x.series_id,
                        principalTable: "series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
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
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
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
                name: "typography_presets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "varchar(200)", nullable: false),
                    font_family = table.Column<string>(type: "varchar(200)", nullable: false),
                    font_size = table.Column<int>(type: "integer", nullable: false),
                    font_weight = table.Column<int>(type: "integer", nullable: false, defaultValue: 400),
                    font_style = table.Column<string>(type: "varchar(50)", nullable: false, defaultValue: "Normal"),
                    line_height = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 1.2m),
                    letter_spacing = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 0m),
                    text_align = table.Column<string>(type: "varchar(30)", nullable: false, defaultValue: "Left"),
                    usage_type = table.Column<string>(type: "varchar(50)", nullable: false, defaultValue: "Custom"),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
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
                    table.PrimaryKey("PK_typography_presets", x => x.id);
                    table.ForeignKey(
                        name: "FK_typography_presets_series_series_id",
                        column: x => x.series_id,
                        principalTable: "series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "external_preview_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "varchar(256)", nullable: false),
                    watermark_text = table.Column<string>(type: "varchar(512)", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    is_revoked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_external_preview_links", x => x.id);
                    table.ForeignKey(
                        name: "FK_external_preview_links_chapters_chapter_id",
                        column: x => x.chapter_id,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_external_preview_links_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "scenes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scene_number = table.Column<int>(type: "int", nullable: false),
                    heading = table.Column<string>(type: "varchar(500)", nullable: true),
                    summary = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_scenes", x => x.id);
                    table.ForeignKey(
                        name: "FK_scenes_chapters_chapter_id",
                        column: x => x.chapter_id,
                        principalTable: "chapters",
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
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
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
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
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
                name: "pages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scene_id = table.Column<Guid>(type: "uuid", nullable: true),
                    page_number = table.Column<int>(type: "int", nullable: false),
                    layout_format = table.Column<string>(type: "varchar(30)", nullable: false),
                    width_px = table.Column<int>(type: "int", nullable: false),
                    height_px = table.Column<int>(type: "int", nullable: false),
                    dpi = table.Column<int>(type: "int", nullable: false, defaultValue: 300),
                    current_stage_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_pages", x => x.id);
                    table.ForeignKey(
                        name: "FK_pages_chapters_chapter_id",
                        column: x => x.chapter_id,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_pages_pipeline_stages_current_stage_id",
                        column: x => x.current_stage_id,
                        principalTable: "pipeline_stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_pages_scenes_scene_id",
                        column: x => x.scene_id,
                        principalTable: "scenes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
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
                    effective_from_chapter_number = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    is_initial_version = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
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
                    table.PrimaryKey("PK_bible_entry_revisions", x => x.id);
                    table.ForeignKey(
                        name: "FK_bible_entry_revisions_bible_entries_bible_entry_id",
                        column: x => x.bible_entry_id,
                        principalTable: "bible_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "script_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scene_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_order = table.Column<int>(type: "int", nullable: false),
                    speaker_character_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dialogue_text = table.Column<string>(type: "text", nullable: true),
                    stage_direction = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_script_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_script_lines_bible_entries_speaker_character_id",
                        column: x => x.speaker_character_id,
                        principalTable: "bible_entries",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_script_lines_scenes_scene_id",
                        column: x => x.scene_id,
                        principalTable: "scenes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "panels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_id = table.Column<Guid>(type: "uuid", nullable: false),
                    panel_number = table.Column<int>(type: "int", nullable: false),
                    bounding_box = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    reading_order = table.Column<int>(type: "int", nullable: false),
                    current_stage_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_panels", x => x.id);
                    table.ForeignKey(
                        name: "FK_panels_pages_page_id",
                        column: x => x.page_id,
                        principalTable: "pages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_panels_pipeline_stages_current_stage_id",
                        column: x => x.current_stage_id,
                        principalTable: "pipeline_stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_usage_records_user_id",
                table: "ai_usage_records",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_usage_records_workspace_occurred",
                table: "ai_usage_records",
                columns: new[] { "workspace_id", "occurred_at_utc" });

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
                name: "ix_audit_logs_action",
                table: "audit_logs",
                column: "action");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_entity_name_entity_id",
                table: "audit_logs",
                columns: new[] { "entity_name", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_timestamp_utc",
                table: "audit_logs",
                column: "timestamp_utc");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_user_id",
                table: "audit_logs",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_workspace_id",
                table: "audit_logs",
                column: "workspace_id");

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
                name: "ix_bible_entry_revisions_effective_from_chapter",
                table: "bible_entry_revisions",
                column: "effective_from_chapter_number");

            migrationBuilder.CreateIndex(
                name: "ix_chapters_series_id",
                table: "chapters",
                column: "series_id");

            migrationBuilder.CreateIndex(
                name: "ix_chapters_series_number",
                table: "chapters",
                columns: new[] { "series_id", "chapter_number" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_chapters_status_series",
                table: "chapters",
                columns: new[] { "status", "series_id" });

            migrationBuilder.CreateIndex(
                name: "ix_consistency_rules_series_id",
                table: "consistency_rules",
                column: "series_id");

            migrationBuilder.CreateIndex(
                name: "ix_element_types_code",
                table: "element_types",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_export_presets_code",
                table: "export_presets",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_external_preview_links_chapter_id",
                table: "external_preview_links",
                column: "chapter_id");

            migrationBuilder.CreateIndex(
                name: "IX_external_preview_links_created_by_user_id",
                table: "external_preview_links",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_external_preview_links_token_hash",
                table: "external_preview_links",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pages_chapter_number",
                table: "pages",
                columns: new[] { "chapter_id", "page_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pages_current_stage_id",
                table: "pages",
                column: "current_stage_id");

            migrationBuilder.CreateIndex(
                name: "IX_pages_scene_id",
                table: "pages",
                column: "scene_id");

            migrationBuilder.CreateIndex(
                name: "IX_panels_current_stage_id",
                table: "panels",
                column: "current_stage_id");

            migrationBuilder.CreateIndex(
                name: "ix_panels_page_number",
                table: "panels",
                columns: new[] { "page_id", "panel_number" },
                unique: true);

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
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_stages_definition_stage_order",
                table: "pipeline_stages",
                columns: new[] { "pipeline_definition_id", "stage_order" });

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_stages_pipeline_definition_id",
                table: "pipeline_stages",
                column: "pipeline_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_template_stages_template_order",
                table: "pipeline_template_stages",
                columns: new[] { "pipeline_template_id", "stage_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_templates_code",
                table: "pipeline_templates",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scenes_chapter_number",
                table: "scenes",
                columns: new[] { "chapter_id", "scene_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_script_lines_scene_order",
                table: "script_lines",
                columns: new[] { "scene_id", "line_order" });

            migrationBuilder.CreateIndex(
                name: "IX_script_lines_speaker_character_id",
                table: "script_lines",
                column: "speaker_character_id");

            migrationBuilder.CreateIndex(
                name: "ix_series_workspace_id",
                table: "series",
                column: "workspace_id");

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
                unique: true,
                filter: "is_deleted = false");

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
                name: "ix_studio_workspaces_owner_id",
                table: "studio_workspaces",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_typography_presets_series_id",
                table: "typography_presets",
                column: "series_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_remembered_devices_user_token",
                table: "user_remembered_devices",
                columns: new[] { "user_id", "token_hash" });

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_firebase_uid",
                table: "users",
                column: "firebase_uid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workspace_ai_configs_workspace_id",
                table: "workspace_ai_configs",
                column: "workspace_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_members_user_id",
                table: "workspace_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_workspace_members_workspace_user",
                table: "workspace_members",
                columns: new[] { "workspace_id", "user_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_usage_records");

            migrationBuilder.DropTable(
                name: "assignments");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "bible_entry_revisions");

            migrationBuilder.DropTable(
                name: "consistency_rules");

            migrationBuilder.DropTable(
                name: "element_types");

            migrationBuilder.DropTable(
                name: "export_presets");

            migrationBuilder.DropTable(
                name: "external_preview_links");

            migrationBuilder.DropTable(
                name: "panels");

            migrationBuilder.DropTable(
                name: "pipeline_template_stages");

            migrationBuilder.DropTable(
                name: "script_lines");

            migrationBuilder.DropTable(
                name: "stage_transitions");

            migrationBuilder.DropTable(
                name: "typography_presets");

            migrationBuilder.DropTable(
                name: "user_remembered_devices");

            migrationBuilder.DropTable(
                name: "workspace_ai_configs");

            migrationBuilder.DropTable(
                name: "workspace_members");

            migrationBuilder.DropTable(
                name: "pages");

            migrationBuilder.DropTable(
                name: "pipeline_templates");

            migrationBuilder.DropTable(
                name: "bible_entries");

            migrationBuilder.DropTable(
                name: "pipeline_stages");

            migrationBuilder.DropTable(
                name: "scenes");

            migrationBuilder.DropTable(
                name: "series_bibles");

            migrationBuilder.DropTable(
                name: "pipeline_definitions");

            migrationBuilder.DropTable(
                name: "chapters");

            migrationBuilder.DropTable(
                name: "series");

            migrationBuilder.DropTable(
                name: "studio_workspaces");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
