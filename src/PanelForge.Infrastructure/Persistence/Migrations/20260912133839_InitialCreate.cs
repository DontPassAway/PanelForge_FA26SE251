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
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firebase_uid = table.Column<string>(type: "varchar(128)", nullable: false),
                    email = table.Column<string>(type: "varchar(320)", nullable: false),
                    full_name = table.Column<string>(type: "varchar(255)", nullable: false),
                    phone_number = table.Column<string>(type: "varchar(20)", nullable: true),
                    avatar_url = table.Column<string>(type: "varchar(2048)", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
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
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
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
                name: "series",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "varchar(500)", nullable: false),
                    synopsis = table.Column<string>(type: "text", nullable: true),
                    reading_direction = table.Column<string>(type: "varchar(20)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
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
                name: "workspace_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "varchar(30)", nullable: false),
                    joined_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
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
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
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
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
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
                    summary = table.Column<string>(type: "text", nullable: true)
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
                    dpi = table.Column<int>(type: "int", nullable: false, defaultValue: 300)
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
                        name: "FK_pages_scenes_scene_id",
                        column: x => x.scene_id,
                        principalTable: "scenes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
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
                    stage_direction = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_script_lines", x => x.id);
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
                    current_stage_id = table.Column<Guid>(type: "uuid", nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "elements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    panel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    element_type = table.Column<string>(type: "varchar(20)", nullable: false),
                    script_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    speaker_character_id = table.Column<Guid>(type: "uuid", nullable: true),
                    z_index = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    transform_geometry = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    content = table.Column<string>(type: "text", nullable: true),
                    style_properties = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elements", x => x.id);
                    table.ForeignKey(
                        name: "FK_elements_panels_panel_id",
                        column: x => x.panel_id,
                        principalTable: "panels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_elements_script_lines_script_line_id",
                        column: x => x.script_line_id,
                        principalTable: "script_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chapters_series_id",
                table: "chapters",
                column: "series_id");

            migrationBuilder.CreateIndex(
                name: "ix_chapters_series_number",
                table: "chapters",
                columns: new[] { "series_id", "chapter_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_elements_panel_zindex",
                table: "elements",
                columns: new[] { "panel_id", "z_index" });

            migrationBuilder.CreateIndex(
                name: "IX_elements_script_line_id",
                table: "elements",
                column: "script_line_id");

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
                name: "IX_pages_scene_id",
                table: "pages",
                column: "scene_id");

            migrationBuilder.CreateIndex(
                name: "ix_panels_page_number",
                table: "panels",
                columns: new[] { "page_id", "panel_number" },
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
                name: "ix_series_workspace_id",
                table: "series",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "ix_studio_workspaces_owner_id",
                table: "studio_workspaces",
                column: "owner_id");

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
                name: "elements");

            migrationBuilder.DropTable(
                name: "external_preview_links");

            migrationBuilder.DropTable(
                name: "workspace_members");

            migrationBuilder.DropTable(
                name: "panels");

            migrationBuilder.DropTable(
                name: "script_lines");

            migrationBuilder.DropTable(
                name: "pages");

            migrationBuilder.DropTable(
                name: "scenes");

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
