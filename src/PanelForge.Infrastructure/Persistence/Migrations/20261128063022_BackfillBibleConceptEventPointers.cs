using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Backfill dữ liệu Bible tạo trước migration AddBibleConceptVersionsAndComments (2026-11-14):
    /// migration đó thêm cột concept_version_number / current_concept_event_id nhưng không điền dữ liệu cũ,
    /// nên revision ConceptSubmitted cũ bị 409 khi Editor xác nhận Canon (Q20, BR-06).
    /// Logic giống BibleEntryRevision.SubmitConcept: đánh số v1..vn theo thời gian nộp, con trỏ = lần nộp mới nhất.
    /// Chỉ chạm hàng đang NULL nên chạy lại an toàn.
    /// </summary>
    public partial class BackfillBibleConceptEventPointers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Chỉ đánh số revision chưa có lần nộp nào được đánh số, tránh trùng số với dữ liệu mới.
            migrationBuilder.Sql("""
                WITH numbered AS (
                    SELECT e.id,
                           ROW_NUMBER() OVER (PARTITION BY e.bible_entry_revision_id ORDER BY e.created_at, e.id) AS v
                    FROM bible_revision_events e
                    WHERE e.event_type = 'ConceptSubmitted' AND NOT e.is_deleted
                )
                UPDATE bible_revision_events e
                SET concept_version_number = n.v
                FROM numbered n
                WHERE e.id = n.id
                  AND e.concept_version_number IS NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM bible_revision_events o
                      WHERE o.bible_entry_revision_id = e.bible_entry_revision_id
                        AND o.event_type = 'ConceptSubmitted'
                        AND o.concept_version_number IS NOT NULL);
                """);

            migrationBuilder.Sql("""
                UPDATE bible_entry_revisions r
                SET current_concept_event_id = (
                    SELECT e.id FROM bible_revision_events e
                    WHERE e.bible_entry_revision_id = r.id
                      AND e.event_type = 'ConceptSubmitted' AND NOT e.is_deleted
                    ORDER BY e.created_at DESC, e.id DESC
                    LIMIT 1)
                WHERE r.current_concept_event_id IS NULL
                  AND EXISTS (
                      SELECT 1 FROM bible_revision_events e
                      WHERE e.bible_entry_revision_id = r.id
                        AND e.event_type = 'ConceptSubmitted' AND NOT e.is_deleted);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Backfill dữ liệu: không hoàn tác (không phân biệt được hàng backfill với hàng do SubmitConcept ghi).
        }
    }
}
