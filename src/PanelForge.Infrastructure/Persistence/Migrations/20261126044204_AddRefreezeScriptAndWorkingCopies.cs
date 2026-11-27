using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreezeScriptAndWorkingCopies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "previous_frozen_script_version_id",
                table: "chapters",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "chapter_script_working_copies",
                columns: table => new
                {
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    writer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_json = table.Column<string>(type: "jsonb", nullable: false),
                    content_hash = table.Column<string>(type: "varchar(64)", nullable: false),
                    line_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chapter_script_working_copies", x => x.chapter_id);
                    table.ForeignKey(
                        name: "FK_chapter_script_working_copies_chapters_chapter_id",
                        column: x => x.chapter_id,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_chapter_script_working_copies_script_versions_base_version_~",
                        column: x => x.base_version_id,
                        principalTable: "script_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_chapter_script_working_copies_users_writer_user_id",
                        column: x => x.writer_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chapters_previous_frozen_script_version_id",
                table: "chapters",
                column: "previous_frozen_script_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_chapter_script_working_copies_base_version_id",
                table: "chapter_script_working_copies",
                column: "base_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_chapter_script_working_copies_writer",
                table: "chapter_script_working_copies",
                column: "writer_user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_chapters_script_versions_previous_frozen_script_version_id",
                table: "chapters",
                column: "previous_frozen_script_version_id",
                principalTable: "script_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_chapters_script_versions_previous_frozen_script_version_id",
                table: "chapters");

            migrationBuilder.DropTable(
                name: "chapter_script_working_copies");

            migrationBuilder.DropIndex(
                name: "ix_chapters_previous_frozen_script_version_id",
                table: "chapters");

            migrationBuilder.DropColumn(
                name: "previous_frozen_script_version_id",
                table: "chapters");
        }
    }
}
