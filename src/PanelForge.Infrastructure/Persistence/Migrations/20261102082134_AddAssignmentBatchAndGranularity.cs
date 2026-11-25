using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignmentBatchAndGranularity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "assignment_granularity",
                table: "pipeline_stages",
                type: "varchar(20)",
                nullable: false,
                defaultValue: "Chapter");

            // Stage vẽ/chèn chữ giao theo trang; Script/Review giữ cả chương (khớp PipelineStage.Create)
            migrationBuilder.Sql(
                "UPDATE pipeline_stages SET assignment_granularity = 'Page' WHERE allowed_role IN ('Artist', 'Letterer');");

            migrationBuilder.AddColumn<Guid>(
                name: "batch_id",
                table: "assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_assignments_batch_id",
                table: "assignments",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "uq_active_assignment",
                table: "assignments",
                columns: new[] { "pipeline_stage_id", "target_entity_id", "entity_type" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_assignments_batch_id",
                table: "assignments");

            migrationBuilder.DropIndex(
                name: "uq_active_assignment",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "assignment_granularity",
                table: "pipeline_stages");

            migrationBuilder.DropColumn(
                name: "batch_id",
                table: "assignments");
        }
    }
}
