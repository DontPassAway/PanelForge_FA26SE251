using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentProviderReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "provider_reference",
                table: "payment_transactions",
                type: "varchar(64)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_transactions_provider_reference",
                table: "payment_transactions",
                column: "provider_reference",
                unique: true,
                filter: "provider_reference IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payment_transactions_provider_reference",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "provider_reference",
                table: "payment_transactions");
        }
    }
}
