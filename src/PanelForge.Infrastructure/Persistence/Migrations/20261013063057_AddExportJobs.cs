using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExportJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "export_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    export_preset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    format = table.Column<string>(type: "varchar(50)", nullable: false),
                    status = table.Column<string>(type: "varchar(20)", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_version = table.Column<long>(type: "bigint", nullable: false),
                    snapshot_artwork_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    result_storage_key = table.Column<string>(type: "varchar(500)", nullable: true),
                    result_file_name = table.Column<string>(type: "varchar(255)", nullable: true),
                    result_content_type = table.Column<string>(type: "varchar(100)", nullable: true),
                    result_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    result_sha256 = table.Column<string>(type: "varchar(64)", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    download_count = table.Column<int>(type: "integer", nullable: false),
                    last_downloaded_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
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
                    table.PrimaryKey("PK_export_jobs", x => x.id);
                    table.ForeignKey(
                        name: "FK_export_jobs_chapters_chapter_id",
                        column: x => x.chapter_id,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_export_jobs_export_presets_export_preset_id",
                        column: x => x.export_preset_id,
                        principalTable: "export_presets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_export_jobs_chapter_id",
                table: "export_jobs",
                column: "chapter_id");

            migrationBuilder.CreateIndex(
                name: "IX_export_jobs_export_preset_id",
                table: "export_jobs",
                column: "export_preset_id");

            migrationBuilder.CreateIndex(
                name: "ix_export_jobs_status_created_at",
                table: "export_jobs",
                columns: new[] { "status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "export_jobs");
        }
    }
}
