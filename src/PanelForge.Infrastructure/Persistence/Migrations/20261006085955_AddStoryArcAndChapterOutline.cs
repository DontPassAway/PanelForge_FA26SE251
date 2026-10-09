using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStoryArcAndChapterOutline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "story_arc",
                table: "series",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "outline",
                table: "chapters",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "story_arc",
                table: "series");

            migrationBuilder.DropColumn(
                name: "outline",
                table: "chapters");
        }
    }
}
