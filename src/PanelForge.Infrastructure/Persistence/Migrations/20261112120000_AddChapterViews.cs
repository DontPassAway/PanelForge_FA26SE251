using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <summary>Lượt đọc chương thật (lượt xem, Top Ngày/Tuần/Tháng trên catalog công khai).</summary>
    [DbContext(typeof(PanelForgeDbContext))]
    [Migration("20261112120000_AddChapterViews")]
    public partial class AddChapterViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chapter_views",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    viewer_key = table.Column<string>(type: "varchar(80)", nullable: false),
                    viewed_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chapter_views", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chapter_views_chapter_viewer",
                table: "chapter_views",
                columns: new[] { "chapter_id", "viewer_key", "viewed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_chapter_views_series_viewed_at",
                table: "chapter_views",
                columns: new[] { "series_id", "viewed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "chapter_views");
        }
    }
}
