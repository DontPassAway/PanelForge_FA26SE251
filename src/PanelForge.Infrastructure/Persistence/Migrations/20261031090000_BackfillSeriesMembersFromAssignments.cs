using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PanelForge.Infrastructure.Persistence;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Chỉ dữ liệu (không đổi schema): ai đang có Assignment trong một Series mà chưa là thành viên Series thì được thêm vào,
    /// role = role Workspace. Assignment tạo trước khi có quyền theo Series (series_members) nên người nhận bị 403 trên chính việc của mình.
    /// </summary>
    [DbContext(typeof(PanelForgeDbContext))]
    [Migration("20261031090000_BackfillSeriesMembersFromAssignments")]
    public partial class BackfillSeriesMembersFromAssignments : Migration
    {
        private const string Marker = "backfill:assignments-2026-10-31";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                INSERT INTO series_members (id, series_id, user_id, role, assigned_at, created_at, created_by, is_deleted)
                SELECT gen_random_uuid(), x.series_id, x.user_id, wm.role, now(), now(), '{Marker}', false
                FROM (SELECT DISTINCT a.series_id, a.assignee_user_id AS user_id
                      FROM assignments a WHERE a.is_deleted = false) x
                JOIN series s ON s.id = x.series_id AND s.is_deleted = false
                JOIN workspace_members wm ON wm.workspace_id = s.workspace_id AND wm.user_id = x.user_id
                                         AND wm.is_deleted = false AND wm.status = 'Active'
                WHERE NOT EXISTS (SELECT 1 FROM series_members sm
                                  WHERE sm.series_id = x.series_id AND sm.user_id = x.user_id AND sm.is_deleted = false);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DELETE FROM series_members WHERE created_by = '{Marker}';");
        }
    }
}
