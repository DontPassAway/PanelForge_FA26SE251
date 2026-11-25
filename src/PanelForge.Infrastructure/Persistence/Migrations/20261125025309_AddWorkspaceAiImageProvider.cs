using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceAiImageProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "image_provider_credential_id",
                table: "workspace_ai_configs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_ai_configs_image_provider_credential_id",
                table: "workspace_ai_configs",
                column: "image_provider_credential_id");

            migrationBuilder.AddForeignKey(
                name: "FK_workspace_ai_configs_ai_provider_credentials_image_provider~",
                table: "workspace_ai_configs",
                column: "image_provider_credential_id",
                principalTable: "ai_provider_credentials",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_workspace_ai_configs_ai_provider_credentials_image_provider~",
                table: "workspace_ai_configs");

            migrationBuilder.DropIndex(
                name: "IX_workspace_ai_configs_image_provider_credential_id",
                table: "workspace_ai_configs");

            migrationBuilder.DropColumn(
                name: "image_provider_credential_id",
                table: "workspace_ai_configs");
        }
    }
}
