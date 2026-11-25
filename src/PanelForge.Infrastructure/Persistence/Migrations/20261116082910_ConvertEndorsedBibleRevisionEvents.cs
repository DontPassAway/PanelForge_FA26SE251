using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConvertEndorsedBibleRevisionEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // b044dc6 bỏ Endorsed khỏi BibleRevisionEventType (Q20) mà không chuyển dữ liệu; giữ lịch sử, đổi sang ConceptSubmitted
            // như cách chuyển trạng thái WriterEndorsed. Chạy lại an toàn.
            migrationBuilder.Sql("UPDATE bible_revision_events SET event_type = 'ConceptSubmitted' WHERE event_type = 'Endorsed';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
