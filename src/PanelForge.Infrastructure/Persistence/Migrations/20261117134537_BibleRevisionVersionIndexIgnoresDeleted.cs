using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BibleRevisionVersionIndexIgnoresDeleted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bible_entry_revisions_bible_entry_id_version",
                table: "bible_entry_revisions");

            migrationBuilder.CreateIndex(
                name: "ix_bible_entry_revisions_bible_entry_id_version",
                table: "bible_entry_revisions",
                columns: new[] { "bible_entry_id", "version_number" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bible_entry_revisions_bible_entry_id_version",
                table: "bible_entry_revisions");

            migrationBuilder.CreateIndex(
                name: "ix_bible_entry_revisions_bible_entry_id_version",
                table: "bible_entry_revisions",
                columns: new[] { "bible_entry_id", "version_number" },
                unique: true);
        }
    }
}
