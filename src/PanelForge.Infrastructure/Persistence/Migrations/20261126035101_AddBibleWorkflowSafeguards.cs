using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBibleWorkflowSafeguards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "allow_solo_creator_review",
                table: "series",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "writer_endorsed_at",
                table: "bible_entry_revisions",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "writer_endorsed_by_user_id",
                table: "bible_entry_revisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "writer_endorsed_concept_version",
                table: "bible_entry_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "aliases",
                table: "bible_entries",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "allow_solo_creator_review",
                table: "series");

            migrationBuilder.DropColumn(
                name: "writer_endorsed_at",
                table: "bible_entry_revisions");

            migrationBuilder.DropColumn(
                name: "writer_endorsed_by_user_id",
                table: "bible_entry_revisions");

            migrationBuilder.DropColumn(
                name: "writer_endorsed_concept_version",
                table: "bible_entry_revisions");

            migrationBuilder.DropColumn(
                name: "aliases",
                table: "bible_entries");
        }
    }
}
