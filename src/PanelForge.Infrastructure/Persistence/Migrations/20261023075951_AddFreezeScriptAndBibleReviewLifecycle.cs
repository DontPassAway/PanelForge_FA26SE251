using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFreezeScriptAndBibleReviewLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_series_members_series_user",
                table: "series_members");

            migrationBuilder.AddColumn<bool>(
                name: "is_script_frozen",
                table: "chapters",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "script_frozen_at",
                table: "chapters",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "script_frozen_by_id",
                table: "chapters",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "artist_user_id",
                table: "bible_entry_revisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review_status",
                table: "bible_entry_revisions",
                type: "varchar(30)",
                nullable: false,
                defaultValue: "Draft");

            migrationBuilder.AddColumn<string>(
                name: "rework_feedback",
                table: "bible_entry_revisions",
                type: "varchar(1000)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_series_members_series_user",
                table: "series_members",
                columns: new[] { "series_id", "user_id" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_series_members_series_user",
                table: "series_members");

            migrationBuilder.DropColumn(
                name: "is_script_frozen",
                table: "chapters");

            migrationBuilder.DropColumn(
                name: "script_frozen_at",
                table: "chapters");

            migrationBuilder.DropColumn(
                name: "script_frozen_by_id",
                table: "chapters");

            migrationBuilder.DropColumn(
                name: "artist_user_id",
                table: "bible_entry_revisions");

            migrationBuilder.DropColumn(
                name: "review_status",
                table: "bible_entry_revisions");

            migrationBuilder.DropColumn(
                name: "rework_feedback",
                table: "bible_entry_revisions");

            migrationBuilder.CreateIndex(
                name: "ix_series_members_series_user",
                table: "series_members",
                columns: new[] { "series_id", "user_id" },
                unique: true);
        }
    }
}
