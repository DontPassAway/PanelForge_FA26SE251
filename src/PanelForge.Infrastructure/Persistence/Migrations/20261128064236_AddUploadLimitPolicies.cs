using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadLimitPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "upload_limit_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    artwork_max_bytes = table.Column<long>(type: "bigint", nullable: false),
                    reference_image_max_bytes = table.Column<long>(type: "bigint", nullable: false),
                    cover_image_max_bytes = table.Column<long>(type: "bigint", nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_upload_limit_policies", x => x.id);
                    table.CheckConstraint("ck_upload_limit_policies_artwork", "artwork_max_bytes > 0 AND artwork_max_bytes <= 52428800");
                    table.CheckConstraint("ck_upload_limit_policies_cover_image", "cover_image_max_bytes > 0 AND cover_image_max_bytes <= 20971520");
                    table.CheckConstraint("ck_upload_limit_policies_reference_image", "reference_image_max_bytes > 0 AND reference_image_max_bytes <= 20971520");
                    table.ForeignKey(
                        name: "FK_upload_limit_policies_users_changed_by_user_id",
                        column: x => x.changed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_upload_limit_policies_changed_by_user_id",
                table: "upload_limit_policies",
                column: "changed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_upload_limit_policies_created_at",
                table: "upload_limit_policies",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "upload_limit_policies");
        }
    }
}
