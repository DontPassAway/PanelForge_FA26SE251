using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChapterStatusAndPublicCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_chapters_series_number",
                table: "chapters");

            migrationBuilder.AddColumn<DateTime>(
                name: "published_at",
                table: "chapters",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "chapters",
                type: "varchar(20)",
                nullable: false,
                defaultValue: "InProduction");

            migrationBuilder.CreateIndex(
                name: "ix_chapters_series_number",
                table: "chapters",
                columns: new[] { "series_id", "chapter_number" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "ix_chapters_status_series",
                table: "chapters",
                columns: new[] { "status", "series_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_chapters_series_number",
                table: "chapters");

            migrationBuilder.DropIndex(
                name: "ix_chapters_status_series",
                table: "chapters");

            migrationBuilder.DropColumn(
                name: "published_at",
                table: "chapters");

            migrationBuilder.DropColumn(
                name: "status",
                table: "chapters");

            migrationBuilder.CreateIndex(
                name: "ix_chapters_series_number",
                table: "chapters",
                columns: new[] { "series_id", "chapter_number" },
                unique: true);
        }
    }
}
