using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// BR-07 / E-11: SystemRole chỉ còn Admin | User.
    /// - Tài khoản đang mang role 'Producer' được chuyển về 'User' và giữ quyền tạo Studio (CanCreateStudio = true).
    ///   Producer thực sự là WorkspaceRole trong bảng workspace_members.
    /// - Administrator không được giữ CanCreateStudio (BR-07, BR-22).
    /// Migration chỉ đổi dữ liệu, không đổi schema.
    /// </summary>
    [DbContext(typeof(PanelForgeDbContext))]
    [Migration("20260926090000_RemoveSystemRoleProducer")]
    public partial class RemoveSystemRoleProducer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE users SET role = 'User', can_create_studio = TRUE WHERE role = 'Producer';");

            migrationBuilder.Sql(
                "UPDATE users SET can_create_studio = FALSE WHERE role = 'Admin';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Không khôi phục: SystemRole.Producer đã bị loại bỏ khỏi domain.
        }
    }
}
