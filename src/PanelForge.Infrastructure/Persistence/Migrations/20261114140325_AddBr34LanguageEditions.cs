using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBr34LanguageEditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "page_translations");

            migrationBuilder.AddColumn<string>(
                name: "preferred_reading_language",
                table: "users",
                type: "varchar(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "original_language",
                table: "series",
                type: "varchar(10)",
                nullable: false,
                defaultValue: "vi");

            migrationBuilder.AddColumn<string>(
                name: "release_languages",
                table: "series",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[\"vi\",\"en\",\"ja\"]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "language_code",
                table: "chapter_snapshots",
                type: "varchar(10)",
                nullable: false,
                defaultValue: "vi");

            migrationBuilder.AddColumn<string>(
                name: "language_code",
                table: "change_requests",
                type: "varchar(10)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "chapter_editions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    language_code = table.Column<string>(type: "varchar(10)", nullable: false),
                    status = table.Column<string>(type: "varchar(20)", nullable: false, defaultValue: "Draft"),
                    is_paid = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    price_vnd = table.Column<decimal>(type: "numeric(12,2)", nullable: false, defaultValue: 0m),
                    free_preview_pages = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    current_snapshot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    translator_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
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
                    table.PrimaryKey("PK_chapter_editions", x => x.id);
                    table.ForeignKey(
                        name: "FK_chapter_editions_chapter_snapshots_current_snapshot_id",
                        column: x => x.current_snapshot_id,
                        principalTable: "chapter_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_chapter_editions_chapters_chapter_id",
                        column: x => x.chapter_id,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_chapter_editions_users_translator_user_id",
                        column: x => x.translator_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chapter_editions_current_snapshot_id",
                table: "chapter_editions",
                column: "current_snapshot_id");

            migrationBuilder.CreateIndex(
                name: "IX_chapter_editions_translator_user_id",
                table: "chapter_editions",
                column: "translator_user_id");

            migrationBuilder.CreateIndex(
                name: "uq_chapter_edition",
                table: "chapter_editions",
                columns: new[] { "chapter_id", "language_code" },
                unique: true);

            // BR-34 backfill: mỗi chương có sẵn một ấn bản cho mỗi ngôn ngữ phát hành của Series.
            // Ấn bản gốc theo trạng thái chương + snapshot mới nhất; ấn bản dịch bắt đầu Draft
            // (ảnh dịch cũ trong page_translations không chuyển được sang bản chữ theo bóng thoại — Q28).
            migrationBuilder.Sql(@"
                INSERT INTO chapter_editions (id, chapter_id, language_code, status, is_paid, price_vnd,
                                              free_preview_pages, current_snapshot_id, version, created_at, is_deleted)
                SELECT gen_random_uuid(), c.id, l.lang,
                       CASE WHEN l.lang <> s.original_language THEN 'Draft'
                            WHEN c.status = 'Published' THEN 'Published'
                            WHEN c.status IN ('Approved', 'Locked') THEN 'Approved'
                            ELSE 'Draft' END,
                       c.is_locked, c.price_vnd, 0,
                       CASE WHEN l.lang = s.original_language AND c.status IN ('Approved', 'Locked', 'Published')
                            THEN (SELECT cs.id FROM chapter_snapshots cs WHERE cs.chapter_id = c.id
                                  ORDER BY cs.snapshot_number DESC LIMIT 1) END,
                       0, NOW(), false
                FROM chapters c
                JOIN series s ON s.id = c.series_id
                CROSS JOIN LATERAL jsonb_array_elements_text(s.release_languages) AS l(lang)
                WHERE NOT c.is_deleted
                ON CONFLICT (chapter_id, language_code) DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chapter_editions");

            migrationBuilder.DropColumn(
                name: "preferred_reading_language",
                table: "users");

            migrationBuilder.DropColumn(
                name: "original_language",
                table: "series");

            migrationBuilder.DropColumn(
                name: "release_languages",
                table: "series");

            migrationBuilder.DropColumn(
                name: "language_code",
                table: "chapter_snapshots");

            migrationBuilder.DropColumn(
                name: "language_code",
                table: "change_requests");

            migrationBuilder.CreateTable(
                name: "page_translations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_sha256 = table.Column<string>(type: "varchar(64)", nullable: false),
                    content_type = table.Column<string>(type: "varchar(50)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    language = table.Column<string>(type: "varchar(8)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_page_translations", x => x.id);
                    table.ForeignKey(
                        name: "FK_page_translations_pages_page_id",
                        column: x => x.page_id,
                        principalTable: "pages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_page_translations_chapter_language",
                table: "page_translations",
                columns: new[] { "chapter_id", "language" });

            migrationBuilder.CreateIndex(
                name: "ix_page_translations_page_language",
                table: "page_translations",
                columns: new[] { "page_id", "language" },
                unique: true,
                filter: "is_deleted = false");
        }
    }
}
