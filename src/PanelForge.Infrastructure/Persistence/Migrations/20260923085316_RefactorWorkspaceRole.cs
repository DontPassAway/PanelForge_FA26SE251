using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RefactorWorkspaceRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data Migration: Cập nhật workspace_members
            migrationBuilder.Sql(@"
                UPDATE workspace_members 
                SET role = 'Artist' 
                WHERE role IN ('Penciler', 'Inker', 'Colorist');
            ");

            migrationBuilder.Sql(@"
                UPDATE workspace_members 
                SET role = 'Editor' 
                WHERE role = 'Reviewer';
            ");

            // Data Migration: Cập nhật assignments
            migrationBuilder.Sql(@"
                UPDATE assignments 
                SET assigned_role = 'Artist' 
                WHERE assigned_role IN ('Penciler', 'Inker', 'Colorist');
            ");

            migrationBuilder.Sql(@"
                UPDATE assignments 
                SET assigned_role = 'Editor' 
                WHERE assigned_role = 'Reviewer';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down is inherently lossy for this schema change
        }
    }
}
