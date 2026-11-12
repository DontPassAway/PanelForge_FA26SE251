using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <summary>Trạng thái hoàn thành của bộ truyện (catalog công khai hiển thị "Đang ra" / "Hoàn thành").</summary>
    [DbContext(typeof(PanelForgeDbContext))]
    [Migration("20261112090000_AddSeriesIsCompleted")]
    public partial class AddSeriesIsCompleted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_completed",
                table: "series",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_completed",
                table: "series");
        }
    }
}
