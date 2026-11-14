using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBibleEntryAliases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bible_entry_names");

            migrationBuilder.DropColumn(
                name: "aliases",
                table: "bible_entries");

            migrationBuilder.AddColumn<string>(
                name: "normalized_name",
                table: "bible_entries",
                type: "varchar(200)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(@"
                WITH numbered AS (
                    SELECT id,
                           COALESCE(NULLIF(lower(trim(regexp_replace(name, '\s+', ' ', 'g'))), ''), 'entry') AS norm,
                           ROW_NUMBER() OVER (
                               PARTITION BY series_bible_id, COALESCE(NULLIF(lower(trim(regexp_replace(name, '\s+', ' ', 'g'))), ''), 'entry')
                               ORDER BY created_at ASC, id ASC
                           ) AS rn
                    FROM bible_entries
                )
                UPDATE bible_entries b
                SET normalized_name = CASE
                    WHEN n.rn = 1 THEN n.norm
                    ELSE substring(n.norm || '-' || n.rn from 1 for 200)
                END
                FROM numbered n
                WHERE b.id = n.id;
            ");

            migrationBuilder.CreateIndex(
                name: "ix_bible_entries_series_bible_id_normalized_name",
                table: "bible_entries",
                columns: new[] { "series_bible_id", "normalized_name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bible_entries_series_bible_id_normalized_name",
                table: "bible_entries");

            migrationBuilder.DropColumn(
                name: "normalized_name",
                table: "bible_entries");

            migrationBuilder.AddColumn<List<string>>(
                name: "aliases",
                table: "bible_entries",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.CreateTable(
                name: "bible_entry_names",
                columns: table => new
                {
                    series_bible_id = table.Column<Guid>(type: "uuid", nullable: false),
                    normalized_name = table.Column<string>(type: "varchar(200)", nullable: false),
                    bible_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bible_entry_names", x => new { x.series_bible_id, x.normalized_name });
                    table.ForeignKey(
                        name: "FK_bible_entry_names_bible_entries_bible_entry_id",
                        column: x => x.bible_entry_id,
                        principalTable: "bible_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_bible_entry_names_series_bibles_series_bible_id",
                        column: x => x.series_bible_id,
                        principalTable: "series_bibles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bible_entry_names_entry",
                table: "bible_entry_names",
                column: "bible_entry_id");
        }
    }
}
