using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <summary>V1.2 / SCR-04 &amp; SCR-07: Bổ sung ScriptLineKind, text, speaker_label và Panel.direction.</summary>
    [DbContext(typeof(PanelForgeDbContext))]
    [Migration("20261113031000_AddScriptLineKindAndPanelDirection")]
    public partial class AddScriptLineKindAndPanelDirection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "script_lines",
                type: "varchar(30)",
                nullable: false,
                defaultValue: "Action");

            migrationBuilder.AddColumn<string>(
                name: "text",
                table: "script_lines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "speaker_label",
                table: "script_lines",
                type: "varchar(100)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "direction",
                table: "panels",
                type: "varchar(1000)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "kind",
                table: "script_lines");

            migrationBuilder.DropColumn(
                name: "text",
                table: "script_lines");

            migrationBuilder.DropColumn(
                name: "speaker_label",
                table: "script_lines");

            migrationBuilder.DropColumn(
                name: "direction",
                table: "panels");
        }
    }
}
