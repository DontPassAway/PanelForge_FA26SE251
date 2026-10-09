using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PanelSceneAndScriptLinePanel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "scene_id",
                table: "panels",
                type: "uuid",
                nullable: true);

            // V1.2: Scene → Page chuyển thành Panel → Scene. Giữ liên kết cũ: panel của Page thuộc Scene nào thì mang Scene đó.
            migrationBuilder.Sql(
                "UPDATE panels SET scene_id = pages.scene_id FROM pages WHERE panels.page_id = pages.id AND pages.scene_id IS NOT NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_pages_scenes_scene_id",
                table: "pages");

            migrationBuilder.DropIndex(
                name: "IX_pages_scene_id",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "scene_id",
                table: "pages");

            migrationBuilder.AddColumn<Guid>(
                name: "panel_id",
                table: "script_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_script_lines_panel",
                table: "script_lines",
                column: "panel_id");

            migrationBuilder.CreateIndex(
                name: "ix_panels_scene",
                table: "panels",
                column: "scene_id");

            migrationBuilder.AddForeignKey(
                name: "FK_panels_scenes_scene_id",
                table: "panels",
                column: "scene_id",
                principalTable: "scenes",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_script_lines_panels_panel_id",
                table: "script_lines",
                column: "panel_id",
                principalTable: "panels",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_panels_scenes_scene_id",
                table: "panels");

            migrationBuilder.DropForeignKey(
                name: "FK_script_lines_panels_panel_id",
                table: "script_lines");

            migrationBuilder.DropIndex(
                name: "ix_script_lines_panel",
                table: "script_lines");

            migrationBuilder.DropIndex(
                name: "ix_panels_scene",
                table: "panels");

            migrationBuilder.DropColumn(
                name: "panel_id",
                table: "script_lines");

            migrationBuilder.DropColumn(
                name: "scene_id",
                table: "panels");

            migrationBuilder.AddColumn<Guid>(
                name: "scene_id",
                table: "pages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_pages_scene_id",
                table: "pages",
                column: "scene_id");

            migrationBuilder.AddForeignKey(
                name: "FK_pages_scenes_scene_id",
                table: "pages",
                column: "scene_id",
                principalTable: "scenes",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
