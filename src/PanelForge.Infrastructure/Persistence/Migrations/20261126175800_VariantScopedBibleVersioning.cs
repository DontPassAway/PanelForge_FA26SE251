using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VariantScopedBibleVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Chuẩn hóa dữ liệu cũ: Điền 'Mặc định' cho các hàng stage_name đang null hoặc rỗng
            migrationBuilder.Sql(@"
                UPDATE bible_entry_revisions
                SET stage_name = 'Mặc định'
                WHERE stage_name IS NULL OR TRIM(stage_name) = '';
            ");

            // 4 (chạy trước 2). Xóa unique index cũ (bible_entry_id, version_number) trước khi đánh số lại,
            // nếu không bước 2 sẽ đụng unique khi một entry có nhiều variant.
            migrationBuilder.DropIndex(
                name: "ix_bible_entry_revisions_bible_entry_id_version",
                table: "bible_entry_revisions");

            // 2. Đánh số lại version_number bắt đầu từ 1 cho từng nhóm (bible_entry_id, stage_name)
            // theo thứ tự created_at asc, id asc
            migrationBuilder.Sql(@"
                WITH numbered AS (
                    SELECT id, ROW_NUMBER() OVER (
                        PARTITION BY bible_entry_id, LOWER(TRIM(stage_name))
                        ORDER BY created_at ASC, id ASC
                    ) AS new_ver
                    FROM bible_entry_revisions
                )
                UPDATE bible_entry_revisions r
                SET version_number = n.new_ver
                FROM numbered n
                WHERE r.id = n.id;
            ");

            // 3. Đổi stage_name thành NOT NULL với default 'Mặc định'
            migrationBuilder.AlterColumn<string>(
                name: "stage_name",
                table: "bible_entry_revisions",
                type: "varchar(100)",
                nullable: false,
                defaultValue: "Mặc định",
                oldClrType: typeof(string),
                oldType: "varchar(100)",
                oldNullable: true);

            // 5. Tạo unique index mới (bible_entry_id, stage_name, version_number)
            migrationBuilder.CreateIndex(
                name: "ix_bible_entry_revisions_bible_entry_id_stage_version",
                table: "bible_entry_revisions",
                columns: new[] { "bible_entry_id", "stage_name", "version_number" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bible_entry_revisions_bible_entry_id_stage_version",
                table: "bible_entry_revisions");

            migrationBuilder.CreateIndex(
                name: "ix_bible_entry_revisions_bible_entry_id_version",
                table: "bible_entry_revisions",
                columns: new[] { "bible_entry_id", "version_number" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.AlterColumn<string>(
                name: "stage_name",
                table: "bible_entry_revisions",
                type: "varchar(100)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(100)",
                oldDefaultValue: "Mặc định");
        }
    }
}
