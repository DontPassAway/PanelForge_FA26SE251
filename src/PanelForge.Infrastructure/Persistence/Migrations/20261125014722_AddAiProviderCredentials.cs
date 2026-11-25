using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiProviderCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "provider_credential_id",
                table: "workspace_ai_configs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ai_provider_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "varchar(100)", nullable: false),
                    provider_type = table.Column<string>(type: "varchar(50)", nullable: false),
                    account_id = table.Column<string>(type: "varchar(100)", nullable: true),
                    api_key_encrypted = table.Column<string>(type: "text", nullable: false),
                    text_model = table.Column<string>(type: "varchar(200)", nullable: false),
                    image_model = table.Column<string>(type: "varchar(200)", nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
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
                    table.PrimaryKey("PK_ai_provider_credentials", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_workspace_ai_configs_provider_credential_id",
                table: "workspace_ai_configs",
                column: "provider_credential_id");

            migrationBuilder.AddForeignKey(
                name: "FK_workspace_ai_configs_ai_provider_credentials_provider_crede~",
                table: "workspace_ai_configs",
                column: "provider_credential_id",
                principalTable: "ai_provider_credentials",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_workspace_ai_configs_ai_provider_credentials_provider_crede~",
                table: "workspace_ai_configs");

            migrationBuilder.DropTable(
                name: "ai_provider_credentials");

            migrationBuilder.DropIndex(
                name: "IX_workspace_ai_configs_provider_credential_id",
                table: "workspace_ai_configs");

            migrationBuilder.DropColumn(
                name: "provider_credential_id",
                table: "workspace_ai_configs");
        }
    }
}
