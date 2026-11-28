using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailDeliveryLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_delivery_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_email = table.Column<string>(type: "varchar(254)", nullable: false),
                    mail_type = table.Column<string>(type: "varchar(40)", nullable: false),
                    subject = table.Column<string>(type: "varchar(300)", nullable: false),
                    succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    failure_reason = table.Column<string>(type: "varchar(40)", nullable: true),
                    error_detail = table.Column<string>(type: "varchar(1000)", nullable: true),
                    smtp_server = table.Column<string>(type: "varchar(255)", nullable: true),
                    smtp_port = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("PK_email_delivery_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_delivery_logs_created_at",
                table: "email_delivery_logs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_email_delivery_logs_succeeded_created_at",
                table: "email_delivery_logs",
                columns: new[] { "succeeded", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_delivery_logs");
        }
    }
}
