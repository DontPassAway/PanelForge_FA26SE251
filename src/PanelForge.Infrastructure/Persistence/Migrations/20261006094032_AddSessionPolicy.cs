using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "max_concurrent_sessions",
                table: "studio_workspaces",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "session_idle_timeout_minutes",
                table: "studio_workspaces",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "session_lifetime_minutes",
                table: "studio_workspaces",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "session_id",
                table: "refresh_tokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "session_started_at",
                table: "refresh_tokens",
                type: "timestamptz",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Token đã phát trước migration: mỗi token là một phiên riêng, bắt đầu lúc nó được tạo
            // (nếu không, mọi token cũ cùng SessionId rỗng và bị coi là hết hạn theo lifetime).
            migrationBuilder.Sql("UPDATE refresh_tokens SET session_id = id, session_started_at = created_at;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "max_concurrent_sessions",
                table: "studio_workspaces");

            migrationBuilder.DropColumn(
                name: "session_idle_timeout_minutes",
                table: "studio_workspaces");

            migrationBuilder.DropColumn(
                name: "session_lifetime_minutes",
                table: "studio_workspaces");

            migrationBuilder.DropColumn(
                name: "session_id",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "session_started_at",
                table: "refresh_tokens");
        }
    }
}
