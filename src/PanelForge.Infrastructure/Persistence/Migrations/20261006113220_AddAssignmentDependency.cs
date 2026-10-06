using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignmentDependency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "depends_on_assignment_id",
                table: "assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_assignments_depends_on_assignment_id",
                table: "assignments",
                column: "depends_on_assignment_id");

            migrationBuilder.AddForeignKey(
                name: "FK_assignments_assignments_depends_on_assignment_id",
                table: "assignments",
                column: "depends_on_assignment_id",
                principalTable: "assignments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_assignments_assignments_depends_on_assignment_id",
                table: "assignments");

            migrationBuilder.DropIndex(
                name: "ix_assignments_depends_on_assignment_id",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "depends_on_assignment_id",
                table: "assignments");
        }
    }
}
