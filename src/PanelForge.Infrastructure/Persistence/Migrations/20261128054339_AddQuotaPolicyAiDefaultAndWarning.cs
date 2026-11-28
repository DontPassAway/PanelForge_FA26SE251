using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuotaPolicyAiDefaultAndWarning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ai_enabled_by_default",
                table: "quota_policies",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "warning_threshold_percent",
                table: "quota_policies",
                type: "integer",
                nullable: false,
                defaultValue: 80);

            migrationBuilder.AddCheckConstraint(
                name: "ck_quota_policies_warning_threshold",
                table: "quota_policies",
                sql: "warning_threshold_percent BETWEEN 1 AND 99");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_quota_policies_warning_threshold",
                table: "quota_policies");

            migrationBuilder.DropColumn(
                name: "ai_enabled_by_default",
                table: "quota_policies");

            migrationBuilder.DropColumn(
                name: "warning_threshold_percent",
                table: "quota_policies");
        }
    }
}
