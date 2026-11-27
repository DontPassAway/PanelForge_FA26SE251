using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyBibleEntrySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE bible_entries SET description = '(' || trim(subtitle) || ') ' || description WHERE subtitle IS NOT NULL AND trim(subtitle) <> '';");

            migrationBuilder.DropIndex(
                name: "ix_bible_entries_series_bible_id_code",
                table: "bible_entries");

            migrationBuilder.DropColumn(
                name: "aliases",
                table: "bible_entries");

            migrationBuilder.DropColumn(
                name: "code",
                table: "bible_entries");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "bible_entries");

            migrationBuilder.DropColumn(
                name: "priority",
                table: "bible_entries");

            migrationBuilder.DropColumn(
                name: "strict_check",
                table: "bible_entries");

            migrationBuilder.DropColumn(
                name: "subtitle",
                table: "bible_entries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "aliases",
                table: "bible_entries",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "code",
                table: "bible_entries",
                type: "varchar(50)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "bible_entries",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "priority",
                table: "bible_entries",
                type: "varchar(20)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "strict_check",
                table: "bible_entries",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "subtitle",
                table: "bible_entries",
                type: "varchar(255)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_bible_entries_series_bible_id_code",
                table: "bible_entries",
                columns: new[] { "series_bible_id", "code" },
                unique: true);
        }
    }
}
