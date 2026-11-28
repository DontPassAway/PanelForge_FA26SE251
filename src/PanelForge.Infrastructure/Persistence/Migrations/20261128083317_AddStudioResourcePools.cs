using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStudioResourcePools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_prepaid_token_budget",
                table: "workspace_ai_configs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "studio_resource_pools",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_bytes = table.Column<long>(type: "bigint", nullable: false),
                    tokens = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_studio_resource_pools", x => x.id);
                    table.CheckConstraint("ck_studio_resource_pools_storage_non_negative", "storage_bytes >= 0");
                    table.CheckConstraint("ck_studio_resource_pools_tokens_non_negative", "tokens >= 0");
                    table.ForeignKey(
                        name: "FK_studio_resource_pools_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "studio_resource_prices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_per_gb_vnd = table.Column<int>(type: "integer", nullable: false),
                    price_per_million_tokens_vnd = table.Column<int>(type: "integer", nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_studio_resource_prices", x => x.id);
                    table.CheckConstraint("ck_studio_resource_prices_gb_positive", "price_per_gb_vnd > 0");
                    table.CheckConstraint("ck_studio_resource_prices_tokens_positive", "price_per_million_tokens_vnd > 0");
                    table.ForeignKey(
                        name: "FK_studio_resource_prices_users_changed_by_user_id",
                        column: x => x.changed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_studio_resource_pools_owner_id",
                table: "studio_resource_pools",
                column: "owner_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_studio_resource_prices_changed_by_user_id",
                table: "studio_resource_prices",
                column: "changed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_studio_resource_prices_created_at",
                table: "studio_resource_prices",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "studio_resource_pools");

            migrationBuilder.DropTable(
                name: "studio_resource_prices");

            migrationBuilder.DropColumn(
                name: "is_prepaid_token_budget",
                table: "workspace_ai_configs");
        }
    }
}
