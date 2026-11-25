using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNarrationMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "narration_mode",
                table: "script_lines",
                type: "varchar(30)",
                nullable: false,
                defaultValue: "Present");

            migrationBuilder.AddColumn<string>(
                name: "default_narration_mode",
                table: "scenes",
                type: "varchar(30)",
                nullable: false,
                defaultValue: "Present");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "narration_mode",
                table: "script_lines");

            migrationBuilder.DropColumn(
                name: "default_narration_mode",
                table: "scenes");
        }
    }
}
