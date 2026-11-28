using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiRequestLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "monthly_request_quota",
                table: "workspace_ai_configs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "rate_window_started_at",
                table: "workspace_ai_configs",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "requests_in_rate_window",
                table: "workspace_ai_configs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "requests_per_minute_limit",
                table: "workspace_ai_configs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "used_requests_current_month",
                table: "workspace_ai_configs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "monthly_request_quota",
                table: "quota_policies",
                type: "bigint",
                nullable: false,
                defaultValue: 5000L);

            migrationBuilder.AddColumn<int>(
                name: "requests_per_minute",
                table: "quota_policies",
                type: "integer",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddCheckConstraint(
                name: "ck_quota_policies_requests_per_minute_positive",
                table: "quota_policies",
                sql: "requests_per_minute > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_quota_policies_requests_positive",
                table: "quota_policies",
                sql: "monthly_request_quota > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_quota_policies_requests_per_minute_positive",
                table: "quota_policies");

            migrationBuilder.DropCheckConstraint(
                name: "ck_quota_policies_requests_positive",
                table: "quota_policies");

            migrationBuilder.DropColumn(
                name: "monthly_request_quota",
                table: "workspace_ai_configs");

            migrationBuilder.DropColumn(
                name: "rate_window_started_at",
                table: "workspace_ai_configs");

            migrationBuilder.DropColumn(
                name: "requests_in_rate_window",
                table: "workspace_ai_configs");

            migrationBuilder.DropColumn(
                name: "requests_per_minute_limit",
                table: "workspace_ai_configs");

            migrationBuilder.DropColumn(
                name: "used_requests_current_month",
                table: "workspace_ai_configs");

            migrationBuilder.DropColumn(
                name: "monthly_request_quota",
                table: "quota_policies");

            migrationBuilder.DropColumn(
                name: "requests_per_minute",
                table: "quota_policies");
        }
    }
}
