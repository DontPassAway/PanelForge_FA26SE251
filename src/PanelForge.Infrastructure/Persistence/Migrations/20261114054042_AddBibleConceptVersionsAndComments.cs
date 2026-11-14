using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBibleConceptVersionsAndComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "asset_sha256",
                table: "bible_revision_events",
                type: "varchar(64)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "concept_version_number",
                table: "bible_revision_events",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "current_concept_event_id",
                table: "bible_entry_revisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "bible_concept_comments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concept_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_comment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    region = table.Column<string>(type: "jsonb", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    created_by = table.Column<string>(type: "varchar(100)", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<string>(type: "varchar(100)", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<string>(type: "varchar(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bible_concept_comments", x => x.id);
                    table.ForeignKey(
                        name: "FK_bible_concept_comments_bible_concept_comments_parent_commen~",
                        column: x => x.parent_comment_id,
                        principalTable: "bible_concept_comments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_bible_concept_comments_bible_entry_revisions_revision_id",
                        column: x => x.revision_id,
                        principalTable: "bible_entry_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_bible_concept_comments_bible_revision_events_concept_event_~",
                        column: x => x.concept_event_id,
                        principalTable: "bible_revision_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bible_entry_revisions_current_concept_event_id",
                table: "bible_entry_revisions",
                column: "current_concept_event_id");

            migrationBuilder.CreateIndex(
                name: "IX_bible_concept_comments_concept_event_id",
                table: "bible_concept_comments",
                column: "concept_event_id");

            migrationBuilder.CreateIndex(
                name: "IX_bible_concept_comments_parent_comment_id",
                table: "bible_concept_comments",
                column: "parent_comment_id");

            migrationBuilder.CreateIndex(
                name: "ix_bible_concept_comments_revision",
                table: "bible_concept_comments",
                columns: new[] { "revision_id", "created_at" });

            migrationBuilder.AddForeignKey(
                name: "FK_bible_entry_revisions_bible_revision_events_current_concept~",
                table: "bible_entry_revisions",
                column: "current_concept_event_id",
                principalTable: "bible_revision_events",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_bible_entry_revisions_bible_revision_events_current_concept~",
                table: "bible_entry_revisions");

            migrationBuilder.DropTable(
                name: "bible_concept_comments");

            migrationBuilder.DropIndex(
                name: "IX_bible_entry_revisions_current_concept_event_id",
                table: "bible_entry_revisions");

            migrationBuilder.DropColumn(
                name: "asset_sha256",
                table: "bible_revision_events");

            migrationBuilder.DropColumn(
                name: "concept_version_number",
                table: "bible_revision_events");

            migrationBuilder.DropColumn(
                name: "current_concept_event_id",
                table: "bible_entry_revisions");
        }
    }
}
