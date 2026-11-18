using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyncDefaultAiModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Chỉ đổi Workspace còn danh sách mặc định cũ; danh sách Admin đã chỉnh tay giữ nguyên.
            migrationBuilder.Sql("""
                UPDATE workspace_ai_configs
                SET allowed_models_json = '["gemini-2.5-flash","gemini-2.5-pro","gemini-2.5-flash-image"]'::jsonb
                WHERE allowed_models_json IN (
                    '["gemini-1.5-flash","gemini-1.5-pro"]'::jsonb,
                    '["gemini-2.5-flash","gemini-2.5-pro"]'::jsonb);
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
