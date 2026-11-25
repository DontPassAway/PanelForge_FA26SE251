using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScriptAnchorsToChangeRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "quoted_text",
                table: "change_requests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "scene_id",
                table: "change_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "script_line_id",
                table: "change_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_change_requests_script_line_id",
                table: "change_requests",
                column: "script_line_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_change_requests_script_line_id",
                table: "change_requests");

            migrationBuilder.DropColumn(
                name: "quoted_text",
                table: "change_requests");

            migrationBuilder.DropColumn(
                name: "scene_id",
                table: "change_requests");

            migrationBuilder.DropColumn(
                name: "script_line_id",
                table: "change_requests");
        }
    }
}
