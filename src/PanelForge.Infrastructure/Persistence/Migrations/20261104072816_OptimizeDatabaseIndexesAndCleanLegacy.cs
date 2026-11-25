using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeDatabaseIndexesAndCleanLegacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_pages_chapter_scripts_chapter_script_id",
                table: "pages");

            migrationBuilder.DropForeignKey(
                name: "FK_scenes_chapter_scripts_chapter_script_id",
                table: "scenes");

            migrationBuilder.DropTable(
                name: "external_preview_links");

            migrationBuilder.DropIndex(
                name: "IX_scenes_chapter_script_id",
                table: "scenes");

            migrationBuilder.DropIndex(
                name: "IX_pages_chapter_script_id",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "chapter_script_id",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "chapter_script_id",
                table: "pages");

            migrationBuilder.RenameIndex(
                name: "IX_script_lines_speaker_character_id",
                table: "script_lines",
                newName: "ix_script_lines_speaker_character_id");

            migrationBuilder.RenameIndex(
                name: "IX_refresh_tokens_user_id",
                table: "refresh_tokens",
                newName: "ix_refresh_tokens_user_id");

            migrationBuilder.RenameIndex(
                name: "IX_content_revisions_chapter_id",
                table: "content_revisions",
                newName: "ix_content_revisions_chapter_id");

            migrationBuilder.RenameIndex(
                name: "IX_change_requests_submission_id",
                table: "change_requests",
                newName: "ix_change_requests_submission_id");

            migrationBuilder.Sql("ALTER TABLE workspace_ai_configs ALTER COLUMN allowed_models_json DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE workspace_ai_configs ALTER COLUMN allowed_models_json TYPE jsonb USING (CASE WHEN allowed_models_json IS NULL OR allowed_models_json = '' THEN '[]'::jsonb ELSE allowed_models_json::jsonb END);");
            migrationBuilder.Sql("ALTER TABLE workspace_ai_configs ALTER COLUMN allowed_models_json SET DEFAULT ('[]'::jsonb);");

            migrationBuilder.Sql("ALTER TABLE series ALTER COLUMN release_schedule_json DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE series ALTER COLUMN release_schedule_json TYPE jsonb USING (CASE WHEN release_schedule_json IS NULL OR release_schedule_json = '' THEN NULL ELSE release_schedule_json::jsonb END);");

            migrationBuilder.Sql("ALTER TABLE change_requests ALTER COLUMN region_json DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE change_requests ALTER COLUMN region_json TYPE jsonb USING (CASE WHEN region_json IS NULL OR region_json = '' THEN NULL ELSE region_json::jsonb END);");

            migrationBuilder.Sql("ALTER TABLE bible_entries ALTER COLUMN details_json DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE bible_entries ALTER COLUMN details_json TYPE jsonb USING (CASE WHEN details_json IS NULL OR details_json = '' THEN '{}'::jsonb ELSE details_json::jsonb END);");
            migrationBuilder.Sql("ALTER TABLE bible_entries ALTER COLUMN details_json SET DEFAULT ('{}'::jsonb);");

            migrationBuilder.Sql("ALTER TABLE assignments ALTER COLUMN reference_asset_urls_json DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE assignments ALTER COLUMN reference_asset_urls_json TYPE jsonb USING (CASE WHEN reference_asset_urls_json IS NULL OR reference_asset_urls_json = '' THEN '[]'::jsonb ELSE reference_asset_urls_json::jsonb END);");
            migrationBuilder.Sql("ALTER TABLE assignments ALTER COLUMN reference_asset_urls_json SET DEFAULT ('[]'::jsonb);");

            migrationBuilder.Sql("ALTER TABLE assignments ALTER COLUMN depends_on_assignment_ids_json DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE assignments ALTER COLUMN depends_on_assignment_ids_json TYPE jsonb USING (CASE WHEN depends_on_assignment_ids_json IS NULL OR depends_on_assignment_ids_json = '' THEN '[]'::jsonb ELSE depends_on_assignment_ids_json::jsonb END);");
            migrationBuilder.Sql("ALTER TABLE assignments ALTER COLUMN depends_on_assignment_ids_json SET DEFAULT ('[]'::jsonb);");

            migrationBuilder.CreateIndex(
                name: "ix_users_email_verification_token",
                table: "users",
                column: "email_verification_token");

            migrationBuilder.CreateIndex(
                name: "ix_users_password_reset_token",
                table: "users",
                column: "password_reset_token");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_created",
                table: "notifications",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_change_requests_asset_reference_id",
                table: "change_requests",
                column: "asset_reference_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_email_verification_token",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_password_reset_token",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_notifications_user_created",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_change_requests_asset_reference_id",
                table: "change_requests");

            migrationBuilder.RenameIndex(
                name: "ix_script_lines_speaker_character_id",
                table: "script_lines",
                newName: "IX_script_lines_speaker_character_id");

            migrationBuilder.RenameIndex(
                name: "ix_refresh_tokens_user_id",
                table: "refresh_tokens",
                newName: "IX_refresh_tokens_user_id");

            migrationBuilder.RenameIndex(
                name: "ix_content_revisions_chapter_id",
                table: "content_revisions",
                newName: "IX_content_revisions_chapter_id");

            migrationBuilder.RenameIndex(
                name: "ix_change_requests_submission_id",
                table: "change_requests",
                newName: "IX_change_requests_submission_id");

            migrationBuilder.AlterColumn<string>(
                name: "allowed_models_json",
                table: "workspace_ai_configs",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldDefaultValueSql: "'[]'::jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "release_schedule_json",
                table: "series",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "chapter_script_id",
                table: "scenes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "chapter_script_id",
                table: "pages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "region_json",
                table: "change_requests",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "details_json",
                table: "bible_entries",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldDefaultValueSql: "'{}'::jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "reference_asset_urls_json",
                table: "assignments",
                type: "text",
                nullable: false,
                defaultValue: "[]",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldDefaultValueSql: "'[]'::jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "depends_on_assignment_ids_json",
                table: "assignments",
                type: "text",
                nullable: false,
                defaultValue: "[]",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldDefaultValueSql: "'[]'::jsonb");

            migrationBuilder.CreateTable(
                name: "external_preview_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    is_revoked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    token_hash = table.Column<string>(type: "varchar(256)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    watermark_text = table.Column<string>(type: "varchar(512)", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_scenes_chapter_script_id",
                table: "scenes",
                column: "chapter_script_id");

            migrationBuilder.CreateIndex(
                name: "IX_pages_chapter_script_id",
                table: "pages",
                column: "chapter_script_id");

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

            migrationBuilder.AddForeignKey(
                name: "FK_pages_chapter_scripts_chapter_script_id",
                table: "pages",
                column: "chapter_script_id",
                principalTable: "chapter_scripts",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_scenes_chapter_scripts_chapter_script_id",
                table: "scenes",
                column: "chapter_script_id",
                principalTable: "chapter_scripts",
                principalColumn: "id");
        }
    }
}
