using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SubmissionRevisionRef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<Guid>>(
                name: "artwork_reference_ids",
                table: "submissions",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]"); // lần nộp cũ (trước khi có cột) = không gắn phiên bản

            migrationBuilder.AddColumn<long>(
                name: "chapter_version",
                table: "submissions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Bù cho lần nộp cũ của việc cấp Page: phiên bản mới nhất của từng (page, panel) ở stage đó, tải lên trước lúc nộp
            migrationBuilder.Sql("""
                UPDATE submissions s
                SET artwork_reference_ids = COALESCE((
                    SELECT array_agg(x.id) FROM (
                        SELECT DISTINCT ON (r.page_id, r.panel_id) r.id
                        FROM asset_references r
                        JOIN assignments a ON a.id = s.assignment_id
                        WHERE a.entity_type = 'Page' AND r.page_id = a.target_entity_id
                          AND r.pipeline_stage_id = a.pipeline_stage_id AND r.created_at <= s.submitted_at
                        ORDER BY r.page_id, r.panel_id, r.version_number DESC) x), '{}'::uuid[]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "artwork_reference_ids",
                table: "submissions");

            migrationBuilder.DropColumn(
                name: "chapter_version",
                table: "submissions");
        }
    }
}
