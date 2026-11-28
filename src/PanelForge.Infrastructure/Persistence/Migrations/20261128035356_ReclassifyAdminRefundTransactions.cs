using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReclassifyAdminRefundTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // OPS-06: hoàn tiền Admin trước đây ghi type = Topup (nhận diện bằng bank_bin = ADMIN_REFUND)
            migrationBuilder.Sql(
                "UPDATE payment_transactions SET type = 'Refund' WHERE type = 'Topup' AND bank_bin = 'ADMIN_REFUND';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE payment_transactions SET type = 'Topup' WHERE type = 'Refund' AND bank_bin = 'ADMIN_REFUND';");
        }
    }
}
