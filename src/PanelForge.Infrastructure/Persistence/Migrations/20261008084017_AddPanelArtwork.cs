using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPanelArtwork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_asset_references_page_stage_version",
                table: "asset_references");

            migrationBuilder.AddColumn<Guid>(
                name: "panel_id",
                table: "asset_references",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_asset_references_page_panel_stage_version",
                table: "asset_references",
                columns: new[] { "page_id", "panel_id", "pipeline_stage_id", "version_number" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_asset_references_page_panel_stage_version",
                table: "asset_references");

            migrationBuilder.DropColumn(
                name: "panel_id",
                table: "asset_references");

            migrationBuilder.CreateIndex(
                name: "ix_asset_references_page_stage_version",
                table: "asset_references",
                columns: new[] { "page_id", "pipeline_stage_id", "version_number" },
                unique: true);
        }
    }
}
