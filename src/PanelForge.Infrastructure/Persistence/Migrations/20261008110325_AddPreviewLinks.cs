using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPreviewLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "preview_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_email = table.Column<string>(type: "varchar(320)", nullable: false),
                    token_hash = table.Column<string>(type: "varchar(64)", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoke_reason = table.Column<string>(type: "varchar(200)", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_artwork_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    chapter_version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_preview_links", x => x.id);
                    table.ForeignKey(
                        name: "FK_preview_links_chapters_chapter_id",
                        column: x => x.chapter_id,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "preview_feedback",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    preview_link_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_id = table.Column<Guid>(type: "uuid", nullable: true),
                    panel_id = table.Column<Guid>(type: "uuid", nullable: true),
                    asset_reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    chapter_version = table.Column<long>(type: "bigint", nullable: false),
                    recipient_email = table.Column<string>(type: "varchar(320)", nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "varchar(20)", nullable: false),
                    triage_note = table.Column<string>(type: "text", nullable: true),
                    triaged_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    triaged_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_preview_feedback", x => x.id);
                    table.ForeignKey(
                        name: "FK_preview_feedback_preview_links_preview_link_id",
                        column: x => x.preview_link_id,
                        principalTable: "preview_links",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "preview_link_access_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    preview_link_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "varchar(20)", nullable: false),
                    allowed = table.Column<bool>(type: "boolean", nullable: false),
                    deny_reason = table.Column<string>(type: "varchar(40)", nullable: true),
                    ip_address = table.Column<string>(type: "varchar(64)", nullable: true),
                    accessed_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_preview_link_access_logs", x => x.id);
                    table.ForeignKey(
                        name: "FK_preview_link_access_logs_preview_links_preview_link_id",
                        column: x => x.preview_link_id,
                        principalTable: "preview_links",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_preview_feedback_chapter_status",
                table: "preview_feedback",
                columns: new[] { "chapter_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_preview_feedback_preview_link_id",
                table: "preview_feedback",
                column: "preview_link_id");

            migrationBuilder.CreateIndex(
                name: "ix_preview_link_access_logs_link_id",
                table: "preview_link_access_logs",
                column: "preview_link_id");

            migrationBuilder.CreateIndex(
                name: "ix_preview_links_chapter_id",
                table: "preview_links",
                column: "chapter_id");

            migrationBuilder.CreateIndex(
                name: "ux_preview_links_token_hash",
                table: "preview_links",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "preview_feedback");

            migrationBuilder.DropTable(
                name: "preview_link_access_logs");

            migrationBuilder.DropTable(
                name: "preview_links");
        }
    }
}
