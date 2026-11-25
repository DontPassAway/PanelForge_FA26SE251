using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TempTest : Migration
    {
        /// <inheritdoc />
        // Trùng cột với AddMultipleAssignmentDependencies; idempotent để DB nào đã chạy một trong hai vẫn migrate được
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE assignments ADD COLUMN IF NOT EXISTS depends_on_assignment_ids_json text NOT NULL DEFAULT '[]';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE assignments DROP COLUMN IF EXISTS depends_on_assignment_ids_json;");
        }
    }
}
