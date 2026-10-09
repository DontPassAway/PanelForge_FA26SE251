using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SoftDeleteAwareNumberIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_scenes_chapter_number",
                table: "scenes");

            migrationBuilder.DropIndex(
                name: "ix_panels_page_number",
                table: "panels");

            migrationBuilder.DropIndex(
                name: "ix_pages_chapter_number",
                table: "pages");

            migrationBuilder.CreateIndex(
                name: "ix_scenes_chapter_number",
                table: "scenes",
                columns: new[] { "chapter_id", "scene_number" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_panels_page_number",
                table: "panels",
                columns: new[] { "page_id", "panel_number" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_pages_chapter_number",
                table: "pages",
                columns: new[] { "chapter_id", "page_number" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_scenes_chapter_number",
                table: "scenes");

            migrationBuilder.DropIndex(
                name: "ix_panels_page_number",
                table: "panels");

            migrationBuilder.DropIndex(
                name: "ix_pages_chapter_number",
                table: "pages");

            migrationBuilder.CreateIndex(
                name: "ix_scenes_chapter_number",
                table: "scenes",
                columns: new[] { "chapter_id", "scene_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_panels_page_number",
                table: "panels",
                columns: new[] { "page_id", "panel_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pages_chapter_number",
                table: "pages",
                columns: new[] { "chapter_id", "page_number" },
                unique: true);
        }
    }
}
