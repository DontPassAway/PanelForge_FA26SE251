using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyBibleMechanisms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_script_line_bible_links_bible_entry_revisions_resolved_revi~",
                table: "script_line_bible_links");

            migrationBuilder.DropForeignKey(
                name: "FK_script_lines_bible_entries_speaker_character_id",
                table: "script_lines");

            migrationBuilder.DropIndex(
                name: "ix_script_lines_speaker_character_id",
                table: "script_lines");

            migrationBuilder.DropIndex(
                name: "ix_script_line_bible_links_resolved_revision_id",
                table: "script_line_bible_links");

            migrationBuilder.DropIndex(
                name: "ix_bible_entry_revisions_effective_from_chapter",
                table: "bible_entry_revisions");

            migrationBuilder.DropColumn(
                name: "speaker_character_id",
                table: "script_lines");

            migrationBuilder.DropColumn(
                name: "resolved_revision_id",
                table: "script_line_bible_links");

            migrationBuilder.DropColumn(
                name: "effective_from_chapter_number",
                table: "bible_entry_revisions");

            migrationBuilder.DropColumn(
                name: "effective_to_chapter_number",
                table: "bible_entry_revisions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "speaker_character_id",
                table: "script_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "resolved_revision_id",
                table: "script_line_bible_links",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "effective_from_chapter_number",
                table: "bible_entry_revisions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "effective_to_chapter_number",
                table: "bible_entry_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_script_lines_speaker_character_id",
                table: "script_lines",
                column: "speaker_character_id");

            migrationBuilder.CreateIndex(
                name: "ix_script_line_bible_links_resolved_revision_id",
                table: "script_line_bible_links",
                column: "resolved_revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_bible_entry_revisions_effective_from_chapter",
                table: "bible_entry_revisions",
                column: "effective_from_chapter_number");

            migrationBuilder.AddForeignKey(
                name: "FK_script_line_bible_links_bible_entry_revisions_resolved_revi~",
                table: "script_line_bible_links",
                column: "resolved_revision_id",
                principalTable: "bible_entry_revisions",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_script_lines_bible_entries_speaker_character_id",
                table: "script_lines",
                column: "speaker_character_id",
                principalTable: "bible_entries",
                principalColumn: "id");
        }
    }
}
