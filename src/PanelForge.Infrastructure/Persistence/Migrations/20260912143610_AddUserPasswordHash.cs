using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPasswordHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "firebase_uid",
                table: "users",
                type: "varchar(128)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(128)");

            migrationBuilder.AddColumn<string>(
                name: "password_hash",
                table: "users",
                type: "varchar(255)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "password_hash",
                table: "users");

            migrationBuilder.AlterColumn<string>(
                name: "firebase_uid",
                table: "users",
                type: "varchar(128)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "varchar(128)",
                oldNullable: true);
        }
    }
}
