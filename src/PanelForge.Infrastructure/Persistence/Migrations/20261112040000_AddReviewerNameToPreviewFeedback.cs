using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <summary>Tên người nhận xét tự khai trên link xem thử công khai (Web Review / Mobile).</summary>
    [DbContext(typeof(PanelForgeDbContext))]
    [Migration("20261112040000_AddReviewerNameToPreviewFeedback")]
    public partial class AddReviewerNameToPreviewFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "reviewer_name",
                table: "preview_feedback",
                type: "varchar(80)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reviewer_name",
                table: "preview_feedback");
        }
    }
}
