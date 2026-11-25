using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScriptFlowV25Enhancements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_script_line_bible_links_script_lines_script_line_id",
                table: "script_line_bible_links");

            migrationBuilder.AddColumn<bool>(
                name: "has_script_impact",
                table: "stage_progress",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "impacted_script_line_id",
                table: "stage_progress",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "raw_mention_text",
                table: "script_line_bible_links",
                type: "varchar(100)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "resolved_revision_id",
                table: "script_line_bible_links",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "content_ready_at",
                table: "pages",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "content_ready_by_id",
                table: "pages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_content_ready",
                table: "pages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "script_reopen_reason",
                table: "chapters",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "script_reopen_requested_at",
                table: "chapters",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "script_reopen_requested_by_id",
                table: "chapters",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "script_reopen_status",
                table: "chapters",
                type: "varchar(20)",
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<bool>(
                name: "is_stub",
                table: "bible_entries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "has_script_impact",
                table: "assignments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "impacted_script_line_id",
                table: "assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_script_line_bible_links_resolved_revision_id",
                table: "script_line_bible_links",
                column: "resolved_revision_id");

            migrationBuilder.AddForeignKey(
                name: "FK_script_line_bible_links_bible_entry_revisions_resolved_revi~",
                table: "script_line_bible_links",
                column: "resolved_revision_id",
                principalTable: "bible_entry_revisions",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_script_line_bible_links_script_lines_script_line_id",
                table: "script_line_bible_links",
                column: "script_line_id",
                principalTable: "script_lines",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_script_line_bible_links_bible_entry_revisions_resolved_revi~",
                table: "script_line_bible_links");

            migrationBuilder.DropForeignKey(
                name: "FK_script_line_bible_links_script_lines_script_line_id",
                table: "script_line_bible_links");

            migrationBuilder.DropIndex(
                name: "ix_script_line_bible_links_resolved_revision_id",
                table: "script_line_bible_links");

            migrationBuilder.DropColumn(
                name: "has_script_impact",
                table: "stage_progress");

            migrationBuilder.DropColumn(
                name: "impacted_script_line_id",
                table: "stage_progress");

            migrationBuilder.DropColumn(
                name: "raw_mention_text",
                table: "script_line_bible_links");

            migrationBuilder.DropColumn(
                name: "resolved_revision_id",
                table: "script_line_bible_links");

            migrationBuilder.DropColumn(
                name: "content_ready_at",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "content_ready_by_id",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "is_content_ready",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "script_reopen_reason",
                table: "chapters");

            migrationBuilder.DropColumn(
                name: "script_reopen_requested_at",
                table: "chapters");

            migrationBuilder.DropColumn(
                name: "script_reopen_requested_by_id",
                table: "chapters");

            migrationBuilder.DropColumn(
                name: "script_reopen_status",
                table: "chapters");

            migrationBuilder.DropColumn(
                name: "is_stub",
                table: "bible_entries");

            migrationBuilder.DropColumn(
                name: "has_script_impact",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "impacted_script_line_id",
                table: "assignments");

            migrationBuilder.AddForeignKey(
                name: "FK_script_line_bible_links_script_lines_script_line_id",
                table: "script_line_bible_links",
                column: "script_line_id",
                principalTable: "script_lines",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
