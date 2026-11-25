using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChapterScripts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateTable(
                name: "chapter_scripts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    raw_text = table.Column<string>(type: "text", nullable: false),
                    is_locked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    locked_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    locked_by_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_chapter_scripts", x => x.id);
                    table.ForeignKey(
                        name: "FK_chapter_scripts_chapters_chapter_id",
                        column: x => x.chapter_id,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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
                name: "ix_chapter_scripts_chapter_id",
                table: "chapter_scripts",
                column: "chapter_id",
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_pages_chapter_scripts_chapter_script_id",
                table: "pages");

            migrationBuilder.DropForeignKey(
                name: "FK_scenes_chapter_scripts_chapter_script_id",
                table: "scenes");

            migrationBuilder.DropTable(
                name: "chapter_scripts");

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
        }
    }
}
