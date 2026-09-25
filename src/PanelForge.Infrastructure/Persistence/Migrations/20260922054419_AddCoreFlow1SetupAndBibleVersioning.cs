using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCoreFlow1SetupAndBibleVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "format",
                table: "series",
                type: "varchar(50)",
                nullable: false,
                defaultValue: "Manga");

            migrationBuilder.AddColumn<string>(
                name: "genre",
                table: "series",
                type: "varchar(100)",
                nullable: false,
                defaultValue: "Action");

            migrationBuilder.AddColumn<string>(
                name: "release_schedule_json",
                table: "series",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "effective_from_chapter_number",
                table: "bible_entry_revisions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "is_initial_version",
                table: "bible_entry_revisions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

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
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "series_presets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    typography_presets_json = table.Column<string>(type: "text", nullable: false),
                    consistency_rules_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_series_presets", x => x.id);
                    table.ForeignKey(
                        name: "FK_series_presets_series_series_id",
                        column: x => x.series_id,
                        principalTable: "series",
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
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
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

            migrationBuilder.CreateIndex(
                name: "ix_bible_entry_revisions_effective_from_chapter",
                table: "bible_entry_revisions",
                column: "effective_from_chapter_number");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_action",
                table: "audit_logs",
                column: "action");

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
                name: "ix_series_presets_series_id",
                table: "series_presets",
                column: "series_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workspace_ai_configs_workspace_id",
                table: "workspace_ai_configs",
                column: "workspace_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "series_presets");

            migrationBuilder.DropTable(
                name: "workspace_ai_configs");

            migrationBuilder.DropIndex(
                name: "ix_bible_entry_revisions_effective_from_chapter",
                table: "bible_entry_revisions");

            migrationBuilder.DropColumn(
                name: "format",
                table: "series");

            migrationBuilder.DropColumn(
                name: "genre",
                table: "series");

            migrationBuilder.DropColumn(
                name: "release_schedule_json",
                table: "series");

            migrationBuilder.DropColumn(
                name: "effective_from_chapter_number",
                table: "bible_entry_revisions");

            migrationBuilder.DropColumn(
                name: "is_initial_version",
                table: "bible_entry_revisions");
        }
    }
}
