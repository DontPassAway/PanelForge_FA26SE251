using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RelinkPageLayoutReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_panel_bible_variants_panels_panel_id",
                table: "panel_bible_variants");

            migrationBuilder.DropForeignKey(
                name: "FK_stage_progress_panels_panel_id",
                table: "stage_progress");

            migrationBuilder.DropPrimaryKey(
                name: "PK_panel_bible_variants",
                table: "panel_bible_variants");

            migrationBuilder.DropIndex(
                name: "ix_asset_references_page_panel_stage_version",
                table: "asset_references");

            migrationBuilder.AlterColumn<Guid>(
                name: "panel_id",
                table: "stage_progress",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "panel_id",
                table: "panel_bible_variants",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "id",
                table: "panel_bible_variants",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddPrimaryKey(
                name: "PK_panel_bible_variants",
                table: "panel_bible_variants",
                column: "id");

            migrationBuilder.CreateIndex(
                name: "ix_panel_bible_variants_panel_entry",
                table: "panel_bible_variants",
                columns: new[] { "panel_id", "bible_entry_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asset_references_page_panel_stage_version",
                table: "asset_references",
                columns: new[] { "page_id", "panel_id", "pipeline_stage_id", "version_number" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", true);

            migrationBuilder.AddForeignKey(
                name: "FK_panel_bible_variants_panels_panel_id",
                table: "panel_bible_variants",
                column: "panel_id",
                principalTable: "panels",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_stage_progress_panels_panel_id",
                table: "stage_progress",
                column: "panel_id",
                principalTable: "panels",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_panel_bible_variants_panels_panel_id",
                table: "panel_bible_variants");

            migrationBuilder.DropForeignKey(
                name: "FK_stage_progress_panels_panel_id",
                table: "stage_progress");

            migrationBuilder.DropPrimaryKey(
                name: "PK_panel_bible_variants",
                table: "panel_bible_variants");

            migrationBuilder.DropIndex(
                name: "ix_panel_bible_variants_panel_entry",
                table: "panel_bible_variants");

            migrationBuilder.DropIndex(
                name: "ix_asset_references_page_panel_stage_version",
                table: "asset_references");

            migrationBuilder.DropColumn(
                name: "id",
                table: "panel_bible_variants");

            migrationBuilder.AlterColumn<Guid>(
                name: "panel_id",
                table: "stage_progress",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "panel_id",
                table: "panel_bible_variants",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_panel_bible_variants",
                table: "panel_bible_variants",
                columns: new[] { "panel_id", "bible_entry_id" });

            migrationBuilder.CreateIndex(
                name: "ix_asset_references_page_panel_stage_version",
                table: "asset_references",
                columns: new[] { "page_id", "panel_id", "pipeline_stage_id", "version_number" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.AddForeignKey(
                name: "FK_panel_bible_variants_panels_panel_id",
                table: "panel_bible_variants",
                column: "panel_id",
                principalTable: "panels",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_stage_progress_panels_panel_id",
                table: "stage_progress",
                column: "panel_id",
                principalTable: "panels",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
