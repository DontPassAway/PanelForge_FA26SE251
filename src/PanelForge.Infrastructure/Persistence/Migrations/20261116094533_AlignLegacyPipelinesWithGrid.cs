using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlignLegacyPipelinesWithGrid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Pipeline của series tạo trước template mới: kịch bản có vòng đời riêng (D1/D12) nên Script, Thumbnail không
            // còn là công đoạn lưới; thiếu nhánh A/B (BR-19) nên Letter phải chờ Color; chưa có cổng duyệt Gate 2 theo trang.
            // Chạy lại an toàn. Ô đã duyệt giữ nguyên (lịch sử); ô chưa xong của Script/Thumbnail bị huỷ.
            migrationBuilder.Sql("""
                UPDATE stage_tasks t SET status = 'Cancelled', is_input_outdated = false, outdated_reason = NULL, updated_at = now()
                FROM pipeline_stages s
                WHERE s.id = t.pipeline_stage_id AND s.slug IN ('script', 'thumbnail')
                  AND t.status NOT IN ('Approved', 'Cancelled');

                UPDATE stage_task_lots l SET status = 'Closed', updated_at = now()
                WHERE l.status <> 'Closed'
                  AND NOT EXISTS (SELECT 1 FROM stage_tasks t WHERE t.lot_id = l.id AND t.status <> 'Cancelled');

                UPDATE pipeline_stages SET is_enabled = false, updated_at = now()
                WHERE slug IN ('script', 'thumbnail') AND is_enabled;

                UPDATE pipeline_stages SET track = 'A', updated_at = now() WHERE track IS NULL AND slug IN ('ink', 'color');
                UPDATE pipeline_stages SET track = 'B', updated_at = now() WHERE track IS NULL AND slug = 'letter';

                UPDATE pipeline_stages SET is_approval_gate = true, updated_at = now()
                WHERE NOT is_approval_gate AND is_enabled AND NOT is_deleted AND (
                    slug IN ('letter', 'color')
                    OR (slug = 'ink' AND NOT EXISTS (
                        SELECT 1 FROM pipeline_stages c
                        WHERE c.pipeline_definition_id = pipeline_stages.pipeline_definition_id
                          AND c.slug = 'color' AND c.is_enabled AND NOT c.is_deleted)));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
