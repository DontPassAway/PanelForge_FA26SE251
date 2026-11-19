using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropJapaneseReleaseLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "release_languages",
                table: "series",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[\"vi\",\"en\"]'::jsonb",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldDefaultValueSql: "'[\"vi\",\"en\",\"ja\"]'::jsonb");

            // Stop offering Japanese; existing ja editions/element texts are kept, only hidden.
            migrationBuilder.Sql(
                "UPDATE series SET release_languages = release_languages - 'ja' WHERE release_languages ? 'ja';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "release_languages",
                table: "series",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[\"vi\",\"en\",\"ja\"]'::jsonb",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldDefaultValueSql: "'[\"vi\",\"en\"]'::jsonb");
        }
    }
}
