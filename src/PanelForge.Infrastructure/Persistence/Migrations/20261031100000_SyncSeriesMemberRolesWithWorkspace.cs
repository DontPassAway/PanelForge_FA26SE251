using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PanelForge.Infrastructure.Persistence;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Chỉ dữ liệu, chạy lại an toàn: role của Series Membership = role Workspace (nguồn duy nhất).
    /// Không xoá gì; membership của người không còn Active trong Workspace bị chặn ở tầng quyền (IsSeriesMemberAsync).
    /// </summary>
    [DbContext(typeof(PanelForgeDbContext))]
    [Migration("20261031100000_SyncSeriesMemberRolesWithWorkspace")]
    public partial class SyncSeriesMemberRolesWithWorkspace : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE series_members sm
                SET role = wm.role, updated_at = now(), updated_by = 'sync:workspace-role-2026-10-31'
                FROM series s, workspace_members wm
                WHERE s.id = sm.series_id AND wm.workspace_id = s.workspace_id AND wm.user_id = sm.user_id
                  AND wm.is_deleted = false AND sm.is_deleted = false AND sm.role <> wm.role;
                """);
        }

        // Không khôi phục role cũ: role riêng theo Series không còn là nguồn quyền
        protected override void Down(MigrationBuilder migrationBuilder) { }
    }
}
