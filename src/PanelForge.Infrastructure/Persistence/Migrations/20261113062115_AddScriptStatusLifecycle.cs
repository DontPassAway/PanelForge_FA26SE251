using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// D12: vòng đời kịch bản (script_status Draft ↔ InReview → Frozen) và chương mới mặc định PreProduction.
    /// Cột kind / text / speaker_label / direction đã có ở 20261113031000 (migration đó thiếu snapshot,
    /// snapshot của migration này bổ sung lại) nên không thêm lần nữa.
    /// </summary>
    public partial class AddScriptStatusLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "chapters",
                type: "varchar(20)",
                nullable: false,
                defaultValue: "PreProduction",
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldDefaultValue: "InProduction");

            migrationBuilder.AddColumn<string>(
                name: "script_status",
                table: "chapters",
                type: "varchar(20)",
                nullable: false,
                defaultValue: "Draft");

            migrationBuilder.AddColumn<DateTime>(
                name: "script_submitted_at",
                table: "chapters",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "script_submitted_by_id",
                table: "chapters",
                type: "uuid",
                nullable: true);

            // Chương đang đóng băng giữ đúng trạng thái kịch bản
            migrationBuilder.Sql("UPDATE chapters SET script_status = 'Frozen' WHERE is_script_frozen = true;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "script_status",
                table: "chapters");

            migrationBuilder.DropColumn(
                name: "script_submitted_at",
                table: "chapters");

            migrationBuilder.DropColumn(
                name: "script_submitted_by_id",
                table: "chapters");

            migrationBuilder.Sql("UPDATE chapters SET status = 'InProduction' WHERE status = 'PreProduction';");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "chapters",
                type: "varchar(20)",
                nullable: false,
                defaultValue: "InProduction",
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldDefaultValue: "PreProduction");
        }
    }
}
