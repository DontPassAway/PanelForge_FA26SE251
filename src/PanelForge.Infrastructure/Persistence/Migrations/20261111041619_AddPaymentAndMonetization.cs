using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentAndMonetization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bank_account_name",
                table: "studio_workspaces",
                type: "varchar(200)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_account_no",
                table: "studio_workspaces",
                type: "varchar(50)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_bin",
                table: "studio_workspaces",
                type: "varchar(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_name",
                table: "studio_workspaces",
                type: "varchar(100)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_locked",
                table: "chapters",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "price_vnd",
                table: "chapters",
                type: "numeric(15,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "payment_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: true),
                    series_id = table.Column<Guid>(type: "uuid", nullable: true),
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_code = table.Column<string>(type: "varchar(64)", nullable: false),
                    type = table.Column<string>(type: "varchar(30)", nullable: false),
                    amount_vnd = table.Column<decimal>(type: "numeric(15,2)", nullable: false),
                    status = table.Column<string>(type: "varchar(30)", nullable: false, defaultValue: "Pending"),
                    qr_image_url = table.Column<string>(type: "varchar(1000)", nullable: false),
                    bank_bin = table.Column<string>(type: "varchar(20)", nullable: false),
                    bank_account_no = table.Column<string>(type: "varchar(50)", nullable: false),
                    bank_account_name = table.Column<string>(type: "varchar(200)", nullable: false),
                    confirmed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    confirmed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rejection_reason = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_payment_transactions", x => x.id);
                    table.ForeignKey(
                        name: "FK_payment_transactions_chapters_chapter_id",
                        column: x => x.chapter_id,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_payment_transactions_series_series_id",
                        column: x => x.series_id,
                        principalTable: "series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_payment_transactions_studio_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "studio_workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_payment_transactions_users_confirmed_by_user_id",
                        column: x => x.confirmed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_payment_transactions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reader_wallets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    balance_vnd = table.Column<decimal>(type: "numeric(15,2)", nullable: false, defaultValue: 0m),
                    is_vip = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    vip_expires_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
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
                    table.PrimaryKey("PK_reader_wallets", x => x.id);
                    table.ForeignKey(
                        name: "FK_reader_wallets_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chapter_entitlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    price_paid_vnd = table.Column<decimal>(type: "numeric(15,2)", nullable: false, defaultValue: 0m),
                    unlocked_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
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
                    table.PrimaryKey("PK_chapter_entitlements", x => x.id);
                    table.ForeignKey(
                        name: "FK_chapter_entitlements_chapters_chapter_id",
                        column: x => x.chapter_id,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_chapter_entitlements_payment_transactions_transaction_id",
                        column: x => x.transaction_id,
                        principalTable: "payment_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_chapter_entitlements_series_series_id",
                        column: x => x.series_id,
                        principalTable: "series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_chapter_entitlements_studio_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "studio_workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_chapter_entitlements_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chapter_entitlements_chapter_id",
                table: "chapter_entitlements",
                column: "chapter_id");

            migrationBuilder.CreateIndex(
                name: "IX_chapter_entitlements_series_id",
                table: "chapter_entitlements",
                column: "series_id");

            migrationBuilder.CreateIndex(
                name: "IX_chapter_entitlements_transaction_id",
                table: "chapter_entitlements",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_chapter_entitlements_user_chapter",
                table: "chapter_entitlements",
                columns: new[] { "user_id", "chapter_id" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_chapter_entitlements_workspace_id",
                table: "chapter_entitlements",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_chapter_id",
                table: "payment_transactions",
                column: "chapter_id");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_confirmed_by_user_id",
                table: "payment_transactions",
                column: "confirmed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_transactions_order_code",
                table: "payment_transactions",
                column: "order_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_series_id",
                table: "payment_transactions",
                column: "series_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_transactions_user_status",
                table: "payment_transactions",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_transactions_workspace_status",
                table: "payment_transactions",
                columns: new[] { "workspace_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_reader_wallets_user_id",
                table: "reader_wallets",
                column: "user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chapter_entitlements");

            migrationBuilder.DropTable(
                name: "reader_wallets");

            migrationBuilder.DropTable(
                name: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "bank_account_name",
                table: "studio_workspaces");

            migrationBuilder.DropColumn(
                name: "bank_account_no",
                table: "studio_workspaces");

            migrationBuilder.DropColumn(
                name: "bank_bin",
                table: "studio_workspaces");

            migrationBuilder.DropColumn(
                name: "bank_name",
                table: "studio_workspaces");

            migrationBuilder.DropColumn(
                name: "is_locked",
                table: "chapters");

            migrationBuilder.DropColumn(
                name: "price_vnd",
                table: "chapters");
        }
    }
}
