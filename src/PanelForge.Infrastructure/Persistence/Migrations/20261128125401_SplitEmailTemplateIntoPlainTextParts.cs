using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SplitEmailTemplateIntoPlainTextParts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Mẫu chuyển từ HTML tự do sang các phần chữ thường. Bản HTML cũ giữ nguyên trong body_html (append-only)
            // nhưng đánh dấu is_deleted để không dùng nữa: email quay về nội dung mặc định cho tới khi Admin lưu bản mới.
            migrationBuilder.AlterColumn<string>(
                name: "body_html",
                table: "platform_email_templates",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "greeting",
                table: "platform_email_templates",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "intro",
                table: "platform_email_templates",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "note",
                table: "platform_email_templates",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "signature",
                table: "platform_email_templates",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("UPDATE platform_email_templates SET is_deleted = true, deleted_at = now() WHERE body_html IS NOT NULL AND is_deleted = false;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "greeting",
                table: "platform_email_templates");

            migrationBuilder.DropColumn(
                name: "intro",
                table: "platform_email_templates");

            migrationBuilder.DropColumn(
                name: "note",
                table: "platform_email_templates");

            migrationBuilder.DropColumn(
                name: "signature",
                table: "platform_email_templates");

            migrationBuilder.Sql("UPDATE platform_email_templates SET body_html = '' WHERE body_html IS NULL;");
            migrationBuilder.AlterColumn<string>(
                name: "body_html",
                table: "platform_email_templates",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
