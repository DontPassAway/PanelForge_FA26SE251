using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLanguageEditionsRatingsAndDefaultPageLocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_page_entitlements_user_page",
                table: "page_entitlements");

            migrationBuilder.DropIndex(
                name: "ix_chapter_entitlements_user_chapter",
                table: "chapter_entitlements");

            migrationBuilder.AddColumn<int>(
                name: "default_locked_page_count",
                table: "series",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "default_page_lock_price_vnd",
                table: "series",
                type: "numeric(15,2)",
                nullable: false,
                defaultValue: 2000m);

            migrationBuilder.AddColumn<string>(
                name: "language",
                table: "payment_transactions",
                type: "varchar(8)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "language",
                table: "page_entitlements",
                type: "varchar(8)",
                nullable: false,
                defaultValue: "vi");

            migrationBuilder.AddColumn<bool>(
                name: "page_locks_customized",
                table: "chapters",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "language",
                table: "chapter_views",
                type: "varchar(8)",
                nullable: false,
                defaultValue: "vi");

            migrationBuilder.AddColumn<string>(
                name: "language",
                table: "chapter_entitlements",
                type: "varchar(8)",
                nullable: false,
                defaultValue: "vi");

            migrationBuilder.CreateTable(
                name: "chapter_language_prices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    language = table.Column<string>(type: "varchar(8)", nullable: false),
                    price_vnd = table.Column<decimal>(type: "numeric(15,2)", nullable: false),
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
                    table.PrimaryKey("PK_chapter_language_prices", x => x.id);
                    table.ForeignKey(
                        name: "FK_chapter_language_prices_chapters_chapter_id",
                        column: x => x.chapter_id,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "page_translations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    language = table.Column<string>(type: "varchar(8)", nullable: false),
                    content_sha256 = table.Column<string>(type: "varchar(64)", nullable: false),
                    content_type = table.Column<string>(type: "varchar(50)", nullable: false),
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
                    table.PrimaryKey("PK_page_translations", x => x.id);
                    table.ForeignKey(
                        name: "FK_page_translations_pages_page_id",
                        column: x => x.page_id,
                        principalTable: "pages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "series_ratings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stars = table.Column<int>(type: "int", nullable: false),
                    language = table.Column<string>(type: "varchar(8)", nullable: false, defaultValue: "vi"),
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
                    table.PrimaryKey("PK_series_ratings", x => x.id);
                    table.CheckConstraint("ck_series_ratings_stars", "stars BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_series_ratings_series_series_id",
                        column: x => x.series_id,
                        principalTable: "series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_series_ratings_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_page_entitlements_user_page",
                table: "page_entitlements",
                columns: new[] { "user_id", "page_id", "language" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_chapter_entitlements_user_chapter",
                table: "chapter_entitlements",
                columns: new[] { "user_id", "chapter_id", "language" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_chapter_language_prices_chapter_language",
                table: "chapter_language_prices",
                columns: new[] { "chapter_id", "language" },
                unique: true,
                filter: "is_deleted = false");

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

            migrationBuilder.CreateIndex(
                name: "ix_series_ratings_series_user",
                table: "series_ratings",
                columns: new[] { "series_id", "user_id" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "IX_series_ratings_user_id",
                table: "series_ratings",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chapter_language_prices");

            migrationBuilder.DropTable(
                name: "page_translations");

            migrationBuilder.DropTable(
                name: "series_ratings");

            migrationBuilder.DropIndex(
                name: "ix_page_entitlements_user_page",
                table: "page_entitlements");

            migrationBuilder.DropIndex(
                name: "ix_chapter_entitlements_user_chapter",
                table: "chapter_entitlements");

            migrationBuilder.DropColumn(
                name: "default_locked_page_count",
                table: "series");

            migrationBuilder.DropColumn(
                name: "default_page_lock_price_vnd",
                table: "series");

            migrationBuilder.DropColumn(
                name: "language",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "language",
                table: "page_entitlements");

            migrationBuilder.DropColumn(
                name: "page_locks_customized",
                table: "chapters");

            migrationBuilder.DropColumn(
                name: "language",
                table: "chapter_views");

            migrationBuilder.DropColumn(
                name: "language",
                table: "chapter_entitlements");

            migrationBuilder.CreateIndex(
                name: "ix_page_entitlements_user_page",
                table: "page_entitlements",
                columns: new[] { "user_id", "page_id" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_chapter_entitlements_user_chapter",
                table: "chapter_entitlements",
                columns: new[] { "user_id", "chapter_id" },
                unique: true,
                filter: "is_deleted = false");
        }
    }
}
