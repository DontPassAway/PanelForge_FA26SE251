using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConsistencyFindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consistency_findings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_code = table.Column<string>(type: "varchar(60)", nullable: false),
                    consistency_rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    severity = table.Column<string>(type: "varchar(20)", nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    evidence = table.Column<string>(type: "text", nullable: true),
                    scene_id = table.Column<Guid>(type: "uuid", nullable: true),
                    script_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    bible_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fingerprint = table.Column<string>(type: "varchar(64)", nullable: false),
                    status = table.Column<string>(type: "varchar(30)", nullable: false),
                    triage_note = table.Column<string>(type: "text", nullable: true),
                    triaged_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    triaged_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    last_detected_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
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
                    table.PrimaryKey("PK_consistency_findings", x => x.id);
                    table.ForeignKey(
                        name: "FK_consistency_findings_chapters_chapter_id",
                        column: x => x.chapter_id,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_consistency_findings_chapter_status",
                table: "consistency_findings",
                columns: new[] { "chapter_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_consistency_findings_chapter_fingerprint",
                table: "consistency_findings",
                columns: new[] { "chapter_id", "fingerprint" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consistency_findings");
        }
    }
}
