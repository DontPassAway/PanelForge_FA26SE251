using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConvertPublishedChapterStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // f9d42d5 đổi ChapterStatus.Published thành HandedOffAndPublished mà không chuyển dữ liệu: đọc Series có chương cũ bị 500.
            // Chương đã phát hành thì coi như đã bàn giao (Producer → Publisher). Chạy lại an toàn.
            migrationBuilder.Sql("UPDATE chapters SET status = 'HandedOffAndPublished', is_handed_off = true, handed_off_at = COALESCE(handed_off_at, published_at, updated_at, created_at) WHERE status = 'Published';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
