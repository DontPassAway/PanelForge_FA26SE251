using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddArtworkAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sha256 = table.Column<string>(type: "varchar(64)", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    content_type = table.Column<string>(type: "varchar(100)", nullable: false),
                    storage_key = table.Column<string>(type: "varchar(200)", nullable: false),
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
                    table.PrimaryKey("PK_assets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "asset_references",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pipeline_stage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "int", nullable: false),
                    layer_type = table.Column<string>(type: "varchar(40)", nullable: true),
                    file_name = table.Column<string>(type: "varchar(255)", nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_asset_references", x => x.id);
                    table.ForeignKey(
                        name: "FK_asset_references_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "asset_access_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ip_address = table.Column<string>(type: "varchar(64)", nullable: true),
                    accessed_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
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
                    table.PrimaryKey("PK_asset_access_logs", x => x.id);
                    table.ForeignKey(
                        name: "FK_asset_access_logs_asset_references_asset_reference_id",
                        column: x => x.asset_reference_id,
                        principalTable: "asset_references",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asset_access_logs_reference_id",
                table: "asset_access_logs",
                column: "asset_reference_id");

            migrationBuilder.CreateIndex(
                name: "ix_asset_references_asset_id",
                table: "asset_references",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_asset_references_chapter_id",
                table: "asset_references",
                column: "chapter_id");

            migrationBuilder.CreateIndex(
                name: "ix_asset_references_page_stage_version",
                table: "asset_references",
                columns: new[] { "page_id", "pipeline_stage_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assets_workspace_sha256",
                table: "assets",
                columns: new[] { "workspace_id", "sha256" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asset_access_logs");

            migrationBuilder.DropTable(
                name: "asset_references");

            migrationBuilder.DropTable(
                name: "assets");
        }
    }
}
