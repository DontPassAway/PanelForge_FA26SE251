using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropStudioBankColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Q13 / OPS-10: tiền VietQR vào tài khoản nền tảng (platform_bank_accounts), cột ngân hàng theo Studio không còn dùng.
            // Giữ lại giá trị cũ trong audit_logs trước khi bỏ cột để không mất dấu vết.
            migrationBuilder.Sql("""
                INSERT INTO audit_logs (id, workspace_id, action, entity_name, entity_id, details, timestamp_utc, created_at, updated_at, is_deleted)
                SELECT gen_random_uuid(), w.id, 'STUDIO_BANK_INFO_ARCHIVED', 'StudioWorkspace', w.id::text,
                       'Tài khoản ngân hàng theo Studio trước Q13: '
                         || coalesce(w.bank_name, '') || ' (BIN ' || coalesce(w.bank_bin, '') || ') · '
                         || coalesce(w.bank_account_no, '') || ' · ' || coalesce(w.bank_account_name, ''),
                       now(), now(), now(), false
                FROM studio_workspaces w
                WHERE w.bank_account_no IS NOT NULL OR w.bank_bin IS NOT NULL;
                """);

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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
        }
    }
}
