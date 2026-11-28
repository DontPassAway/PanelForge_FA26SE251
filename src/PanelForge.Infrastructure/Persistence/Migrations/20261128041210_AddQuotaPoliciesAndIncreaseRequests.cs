using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuotaPoliciesAndIncreaseRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "quota_increase_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_type = table.Column<string>(type: "varchar(20)", nullable: false),
                    current_quota = table.Column<long>(type: "bigint", nullable: false),
                    requested_quota = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "varchar(1000)", nullable: false),
                    status = table.Column<string>(type: "varchar(20)", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    review_note = table.Column<string>(type: "varchar(1000)", nullable: true),
                    granted_quota = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_quota_increase_requests", x => x.id);
                    table.CheckConstraint("ck_quota_increase_requests_requested_gt_current", "requested_quota > current_quota");
                    table.ForeignKey(
                        name: "FK_quota_increase_requests_studio_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "studio_workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_quota_increase_requests_users_requested_by_user_id",
                        column: x => x.requested_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_quota_increase_requests_users_reviewed_by_user_id",
                        column: x => x.reviewed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "quota_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_quota_bytes = table.Column<long>(type: "bigint", nullable: false),
                    monthly_token_quota = table.Column<long>(type: "bigint", nullable: false),
                    monthly_image_quota = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_quota_policies", x => x.id);
                    table.CheckConstraint("ck_quota_policies_images_positive", "monthly_image_quota > 0");
                    table.CheckConstraint("ck_quota_policies_storage_positive", "storage_quota_bytes > 0");
                    table.CheckConstraint("ck_quota_policies_tokens_positive", "monthly_token_quota > 0");
                    table.ForeignKey(
                        name: "FK_quota_policies_users_changed_by_user_id",
                        column: x => x.changed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_quota_increase_requests_requested_by_user_id",
                table: "quota_increase_requests",
                column: "requested_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_quota_increase_requests_reviewed_by_user_id",
                table: "quota_increase_requests",
                column: "reviewed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_quota_increase_requests_status_created",
                table: "quota_increase_requests",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_quota_increase_requests_pending",
                table: "quota_increase_requests",
                columns: new[] { "workspace_id", "resource_type" },
                unique: true,
                filter: "status = 'Pending' AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "IX_quota_policies_changed_by_user_id",
                table: "quota_policies",
                column: "changed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_quota_policies_created_at",
                table: "quota_policies",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quota_increase_requests");

            migrationBuilder.DropTable(
                name: "quota_policies");
        }
    }
}
