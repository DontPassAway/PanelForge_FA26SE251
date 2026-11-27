using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssignAutomationFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ready_at",
                table: "stage_tasks",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "auto_assign_reason",
                table: "stage_task_lots",
                type: "varchar(300)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_auto_assigned",
                table: "stage_task_lots",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "workspace_automation_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    auto_next_stage = table.Column<bool>(type: "boolean", nullable: false),
                    assign_mode = table.Column<string>(type: "varchar(20)", nullable: false),
                    auto_route_review = table.Column<bool>(type: "boolean", nullable: false),
                    lead_hours = table.Column<int>(type: "integer", nullable: false),
                    escalation_hours = table.Column<int>(type: "integer", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_automation_settings", x => x.id);
                    table.ForeignKey(
                        name: "FK_workspace_automation_settings_studio_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "studio_workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_workspace_automation_settings_workspace_id",
                table: "workspace_automation_settings",
                column: "workspace_id",
                unique: true,
                filter: "is_deleted = false");

            // N5: gợi ý người duyệt chỉ còn ý nghĩa khi ô đang được duyệt; dọn giá trị cũ còn sót
            // (trước đây Submit/Withdraw/Unassign không xoá cột này).
            migrationBuilder.Sql("UPDATE stage_tasks SET reviewer_user_id = NULL WHERE reviewer_user_id IS NOT NULL AND status <> 'UnderReview';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workspace_automation_settings");

            migrationBuilder.DropColumn(
                name: "ready_at",
                table: "stage_tasks");

            migrationBuilder.DropColumn(
                name: "auto_assign_reason",
                table: "stage_task_lots");

            migrationBuilder.DropColumn(
                name: "is_auto_assigned",
                table: "stage_task_lots");
        }
    }
}
