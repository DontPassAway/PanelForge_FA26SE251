using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SwitchToChapterPreviewPages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "page_entitlements");

            migrationBuilder.DropColumn(
                name: "default_page_lock_price_vnd",
                table: "series");

            migrationBuilder.DropColumn(
                name: "unlock_price_vnd",
                table: "pages");

            migrationBuilder.RenameColumn(
                name: "default_locked_page_count",
                table: "series",
                newName: "default_preview_page_count");

            migrationBuilder.RenameColumn(
                name: "is_paid_locked",
                table: "pages",
                newName: "is_preview");

            migrationBuilder.RenameColumn(
                name: "page_locks_customized",
                table: "chapters",
                newName: "preview_pages_customized");

            // Cột đổi tên mang nghĩa cũ (khóa trang lẻ) → đặt lại theo mô hình xem trước.
            // Bỏ giao dịch mua trang lẻ cũ (enum PageUnlock không còn; người dùng không cần giữ dữ liệu cũ).
            migrationBuilder.Sql("UPDATE pages SET is_preview = FALSE;");
            migrationBuilder.Sql("UPDATE chapters SET preview_pages_customized = FALSE;");
            migrationBuilder.Sql("UPDATE series SET default_preview_page_count = 0;");
            migrationBuilder.Sql("DELETE FROM payment_transactions WHERE type = 'PageUnlock';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "default_preview_page_count",
                table: "series",
                newName: "default_locked_page_count");

            migrationBuilder.RenameColumn(
                name: "is_preview",
                table: "pages",
                newName: "is_paid_locked");

            migrationBuilder.RenameColumn(
                name: "preview_pages_customized",
                table: "chapters",
                newName: "page_locks_customized");

            migrationBuilder.AddColumn<decimal>(
                name: "default_page_lock_price_vnd",
                table: "series",
                type: "numeric(15,2)",
                nullable: false,
                defaultValue: 2000m);

            migrationBuilder.AddColumn<decimal>(
                name: "unlock_price_vnd",
                table: "pages",
                type: "numeric(15,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "page_entitlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    language = table.Column<string>(type: "varchar(8)", nullable: false, defaultValue: "vi"),
                    price_paid_vnd = table.Column<decimal>(type: "numeric(15,2)", nullable: false, defaultValue: 0m),
                    series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unlocked_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_page_entitlements", x => x.id);
                    table.ForeignKey(
                        name: "FK_page_entitlements_pages_page_id",
                        column: x => x.page_id,
                        principalTable: "pages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_page_entitlements_payment_transactions_transaction_id",
                        column: x => x.transaction_id,
                        principalTable: "payment_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_page_entitlements_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_page_entitlements_page_id",
                table: "page_entitlements",
                column: "page_id");

            migrationBuilder.CreateIndex(
                name: "ix_page_entitlements_series_id",
                table: "page_entitlements",
                column: "series_id");

            migrationBuilder.CreateIndex(
                name: "IX_page_entitlements_transaction_id",
                table: "page_entitlements",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_page_entitlements_user_chapter",
                table: "page_entitlements",
                columns: new[] { "user_id", "chapter_id" });

            migrationBuilder.CreateIndex(
                name: "ix_page_entitlements_user_page",
                table: "page_entitlements",
                columns: new[] { "user_id", "page_id", "language" },
                unique: true,
                filter: "is_deleted = false");
        }
    }
}
