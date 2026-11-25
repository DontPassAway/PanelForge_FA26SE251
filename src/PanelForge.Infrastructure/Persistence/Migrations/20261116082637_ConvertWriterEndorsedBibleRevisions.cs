using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConvertWriterEndorsedBibleRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ec2c5e1 bỏ WriterEndorsed khỏi enum (Q20) và xoá câu chuyển dữ liệu khỏi AddBibleConceptVersionsAndComments;
            // DB migrate sau commit đó còn dòng cũ, đọc Bible bị 500. Chạy lại an toàn.
            migrationBuilder.Sql("UPDATE bible_entry_revisions SET review_status = 'ConceptSubmitted' WHERE review_status = 'WriterEndorsed';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
