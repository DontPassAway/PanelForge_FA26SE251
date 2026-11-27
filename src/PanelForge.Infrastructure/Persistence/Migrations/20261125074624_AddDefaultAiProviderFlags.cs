using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDefaultAiProviderFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_default_image",
                table: "ai_provider_credentials",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_default_text",
                table: "ai_provider_credentials",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ux_ai_provider_credentials_default_image",
                table: "ai_provider_credentials",
                column: "is_default_image",
                unique: true,
                filter: "is_default_image AND NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ux_ai_provider_credentials_default_text",
                table: "ai_provider_credentials",
                column: "is_default_text",
                unique: true,
                filter: "is_default_text AND NOT is_deleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_ai_provider_credentials_default_image",
                table: "ai_provider_credentials");

            migrationBuilder.DropIndex(
                name: "ux_ai_provider_credentials_default_text",
                table: "ai_provider_credentials");

            migrationBuilder.DropColumn(
                name: "is_default_image",
                table: "ai_provider_credentials");

            migrationBuilder.DropColumn(
                name: "is_default_text",
                table: "ai_provider_credentials");
        }
    }
}
