using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStageTaskSubmissionSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "canvas_versions_json",
                table: "stage_task_submissions",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "panel_layout_json",
                table: "stage_task_submissions",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<Guid>(
                name: "script_version_id",
                table: "stage_task_submissions",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "canvas_versions_json",
                table: "stage_task_submissions");

            migrationBuilder.DropColumn(
                name: "panel_layout_json",
                table: "stage_task_submissions");

            migrationBuilder.DropColumn(
                name: "script_version_id",
                table: "stage_task_submissions");
        }
    }
}
