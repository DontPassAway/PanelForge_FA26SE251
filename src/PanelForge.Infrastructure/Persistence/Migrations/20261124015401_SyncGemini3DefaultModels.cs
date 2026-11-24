using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyncGemini3DefaultModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Model văn bản mặc định đổi sang gemini-3.8-flash: Workspace còn danh sách mặc định cũ phải có model mới,
            // nếu không mọi lượt chạy AI bị chặn. Danh sách Admin đã chỉnh tay giữ nguyên.
            migrationBuilder.Sql("""
                UPDATE workspace_ai_configs
                SET allowed_models_json = '["gemini-3.8-flash","gemini-3.1-pro-preview","gemini-2.5-flash","gemini-2.5-pro","gemini-2.5-flash-image"]'::jsonb
                WHERE allowed_models_json = '["gemini-2.5-flash","gemini-2.5-pro","gemini-2.5-flash-image"]'::jsonb;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE workspace_ai_configs
                SET allowed_models_json = '["gemini-2.5-flash","gemini-2.5-pro","gemini-2.5-flash-image"]'::jsonb
                WHERE allowed_models_json = '["gemini-3.8-flash","gemini-3.1-pro-preview","gemini-2.5-flash","gemini-2.5-pro","gemini-2.5-flash-image"]'::jsonb;
                """);
        }
    }
}
