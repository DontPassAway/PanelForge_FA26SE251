using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChapterHandOffStateAndFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "chapters",
                type: "varchar(30)",
                nullable: false,
                defaultValue: "PreProduction",
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldDefaultValue: "PreProduction");

            migrationBuilder.AddColumn<DateTime>(
                name: "handed_off_at",
                table: "chapters",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "handed_off_by_id",
                table: "chapters",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_handed_off",
                table: "chapters",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "handed_off_at",
                table: "chapters");

            migrationBuilder.DropColumn(
                name: "handed_off_by_id",
                table: "chapters");

            migrationBuilder.DropColumn(
                name: "is_handed_off",
                table: "chapters");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "chapters",
                type: "varchar(20)",
                nullable: false,
                defaultValue: "PreProduction",
                oldClrType: typeof(string),
                oldType: "varchar(30)",
                oldDefaultValue: "PreProduction");
        }
    }
}
