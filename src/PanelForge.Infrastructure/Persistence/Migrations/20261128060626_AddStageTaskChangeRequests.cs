using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStageTaskChangeRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "assignment_id",
                table: "change_requests",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "stage_task_id",
                table: "change_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_change_requests_stage_task_status",
                table: "change_requests",
                columns: new[] { "stage_task_id", "status" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_change_requests_one_work_item",
                table: "change_requests",
                sql: "num_nonnulls(assignment_id, stage_task_id) = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_change_requests_stage_tasks_stage_task_id",
                table: "change_requests",
                column: "stage_task_id",
                principalTable: "stage_tasks",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_change_requests_stage_tasks_stage_task_id",
                table: "change_requests");

            migrationBuilder.DropIndex(
                name: "ix_change_requests_stage_task_status",
                table: "change_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_change_requests_one_work_item",
                table: "change_requests");

            migrationBuilder.DropColumn(
                name: "stage_task_id",
                table: "change_requests");

            migrationBuilder.AlterColumn<Guid>(
                name: "assignment_id",
                table: "change_requests",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
