using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoveMonetizationToChapterEdition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // BR-33 / D4: loại Free/Trả phí, giá và N trang xem trước chuyển hẳn về chapter_editions.
            migrationBuilder.AddColumn<DateTime>(
                name: "monetization_configured_at",
                table: "chapter_editions",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "monetization_configured_by_id",
                table: "chapter_editions",
                type: "uuid",
                nullable: true);

            // 1) Chương đã phát hành nhưng chưa có ấn bản ngôn ngữ gốc → tạo ấn bản Published (giữ chương đang đọc được).
            migrationBuilder.Sql("""
                INSERT INTO chapter_editions (id, chapter_id, language_code, status, is_paid, price_vnd, free_preview_pages,
                                              published_at, version, created_at, is_deleted)
                SELECT gen_random_uuid(), c.id, s.original_language, 'Published', false, 0, 0, c.published_at, 0, now(), false
                FROM chapters c
                JOIN series s ON s.id = c.series_id
                WHERE c.status = 'HandedOffAndPublished'
                  AND NOT EXISTS (SELECT 1 FROM chapter_editions e
                                  WHERE e.chapter_id = c.id AND e.language_code = s.original_language);
                """);

            // 2) Chép cấu hình cũ (mức chương + giá riêng theo ngôn ngữ + trang xem trước) vào từng ấn bản.
            //    Giá cũ bằng 0 trên chương khóa từng được tính 10.000đ khi mở khóa → giữ nguyên 10.000đ.
            //    Xem trước: chương theo mặc định bộ truyện → N = default_preview_page_count; chọn tay → số trang
            //    xem trước liền nhau từ trang đầu. N luôn < tổng số trang.
            //    Ấn bản đang Published đã bán theo cấu hình này → đánh dấu đã cấu hình (D4); ấn bản khác chờ Publisher chốt.
            migrationBuilder.Sql("""
                UPDATE chapter_editions ce
                SET is_paid = c.is_locked,
                    price_vnd = CASE WHEN c.is_locked THEN COALESCE(
                        NULLIF((SELECT p.price_vnd FROM chapter_language_prices p
                                WHERE p.chapter_id = c.id AND p.language = ce.language_code AND NOT p.is_deleted
                                LIMIT 1), 0),
                        NULLIF(c.price_vnd, 0),
                        10000) ELSE 0 END,
                    free_preview_pages = CASE WHEN c.is_locked THEN LEAST(
                        CASE WHEN c.preview_pages_customized THEN (
                            SELECT COUNT(*) FROM pages p
                            WHERE p.chapter_id = c.id AND p.is_preview
                              AND NOT EXISTS (SELECT 1 FROM pages q
                                              WHERE q.chapter_id = c.id AND NOT q.is_preview AND q.page_number < p.page_number))
                        ELSE s.default_preview_page_count END,
                        GREATEST((SELECT COUNT(*) FROM pages p WHERE p.chapter_id = c.id) - 1, 0)) ELSE 0 END,
                    monetization_configured_at = CASE WHEN ce.status = 'Published' THEN now() ELSE NULL END
                FROM chapters c
                JOIN series s ON s.id = c.series_id
                WHERE ce.chapter_id = c.id;
                """);

            migrationBuilder.DropTable(
                name: "chapter_language_prices");

            migrationBuilder.DropColumn(
                name: "default_preview_page_count",
                table: "series");

            migrationBuilder.DropColumn(
                name: "is_preview",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "is_locked",
                table: "chapters");

            migrationBuilder.DropColumn(
                name: "preview_pages_customized",
                table: "chapters");

            migrationBuilder.DropColumn(
                name: "price_vnd",
                table: "chapters");

            migrationBuilder.AddCheckConstraint(
                name: "ck_chapter_editions_free_has_no_price",
                table: "chapter_editions",
                sql: "is_paid OR price_vnd = 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_chapter_editions_preview_non_negative",
                table: "chapter_editions",
                sql: "free_preview_pages >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_chapter_editions_price_non_negative",
                table: "chapter_editions",
                sql: "price_vnd >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_chapter_editions_free_has_no_price",
                table: "chapter_editions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_chapter_editions_preview_non_negative",
                table: "chapter_editions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_chapter_editions_price_non_negative",
                table: "chapter_editions");

            migrationBuilder.DropColumn(
                name: "monetization_configured_at",
                table: "chapter_editions");

            migrationBuilder.DropColumn(
                name: "monetization_configured_by_id",
                table: "chapter_editions");

            migrationBuilder.AddColumn<int>(
                name: "default_preview_page_count",
                table: "series",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "is_preview",
                table: "pages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_locked",
                table: "chapters",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "preview_pages_customized",
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
                name: "chapter_language_prices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    language = table.Column<string>(type: "varchar(8)", nullable: false),
                    price_vnd = table.Column<decimal>(type: "numeric(15,2)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chapter_language_prices", x => x.id);
                    table.ForeignKey(
                        name: "FK_chapter_language_prices_chapters_chapter_id",
                        column: x => x.chapter_id,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chapter_language_prices_chapter_language",
                table: "chapter_language_prices",
                columns: new[] { "chapter_id", "language" },
                unique: true,
                filter: "is_deleted = false");
        }
    }
}
