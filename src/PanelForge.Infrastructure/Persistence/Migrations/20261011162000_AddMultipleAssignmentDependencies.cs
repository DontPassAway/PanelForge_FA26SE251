using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMultipleAssignmentDependencies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE assignments ADD COLUMN IF NOT EXISTS depends_on_assignment_ids_json text NOT NULL DEFAULT '[]';");

            // Tự động đồng bộ các bản ghi cũ có depends_on_assignment_id sang JSON array
            migrationBuilder.Sql(@"
                UPDATE assignments 
                SET depends_on_assignment_ids_json = json_build_array(depends_on_assignment_id)::text 
                WHERE depends_on_assignment_id IS NOT NULL 
                  AND (depends_on_assignment_ids_json IS NULL OR depends_on_assignment_ids_json = '[]');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "depends_on_assignment_ids_json",
                table: "assignments");
        }
    }
}
