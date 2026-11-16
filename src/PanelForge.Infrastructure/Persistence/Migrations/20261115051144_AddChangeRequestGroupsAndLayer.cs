using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChangeRequestGroupsAndLayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "group_id",
                table: "change_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "layer",
                table: "change_requests",
                type: "varchar(20)",
                nullable: false,
                defaultValue: "Artwork");

            migrationBuilder.CreateIndex(
                name: "ix_change_requests_group_id",
                table: "change_requests",
                column: "group_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_change_requests_group_id",
                table: "change_requests");

            migrationBuilder.DropColumn(
                name: "group_id",
                table: "change_requests");

            migrationBuilder.DropColumn(
                name: "layer",
                table: "change_requests");
        }
    }
}
