using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Sửa dữ liệu (không đổi schema): Series Manga tạo trước 28/11 có Color tắt nhưng vẫn giữ cổng duyệt, Ink cùng nhánh
    /// không có cổng, nên ô Ink tự duyệt khi nộp và tranh không qua Editor theo trang (Gate 2, BR-19).
    /// Chuyển cổng sang bước còn bật liền trước cùng nhánh, đúng luật của PipelineDefinition.MoveGateOffDisabledStage.
    /// </summary>
    [DbContext(typeof(PanelForgeDbContext))]
    [Migration("20261128050000_MoveMangaColorGateToInk")]
    public partial class MoveMangaColorGateToInk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE pipeline_stages AS prev
                SET is_approval_gate = TRUE, updated_at = now()
                FROM pipeline_stages AS off
                WHERE off.is_enabled = FALSE
                  AND off.is_approval_gate = TRUE
                  AND off.track IS NOT NULL
                  AND prev.pipeline_definition_id = off.pipeline_definition_id
                  AND prev.is_enabled = TRUE
                  AND prev.is_approval_gate = FALSE
                  AND prev.track = off.track
                  AND prev.stage_order = (
                      SELECT MAX(s.stage_order) FROM pipeline_stages s
                      WHERE s.pipeline_definition_id = off.pipeline_definition_id
                        AND s.is_enabled = TRUE AND s.track = off.track AND s.stage_order < off.stage_order);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Không hoàn tác: không phân biệt được cổng do migration này bật với cổng Producer tự bật sau đó.
        }
    }
}
