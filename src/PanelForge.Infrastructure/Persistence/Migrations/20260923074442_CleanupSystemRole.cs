using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CleanupSystemRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Chuẩn hóa cột role trên bảng users về 2 giá trị hợp lệ: 'Admin', 'User'
            // Mọi giá trị không phải 'Admin' (bao gồm Moderator, Producer, Writer,
            // Artist, Letterer, Editor, Reader, User) đều được giữ nguyên nếu là 'User',
            // hoặc chuyển về 'User' nếu là giá trị lạ/cũ không còn hợp lệ.
            migrationBuilder.Sql(@"
                UPDATE users
                SET role = 'User'
                WHERE role NOT IN ('Admin', 'User');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Không thể khôi phục thông tin role cụ thể đã bị xóa, Down là no-op.
        }
    }
}
