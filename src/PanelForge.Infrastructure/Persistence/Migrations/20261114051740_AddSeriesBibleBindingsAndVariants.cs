using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSeriesBibleBindingsAndVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // BIB-11, 5A: Đổi raw_mention_text (varchar 100) -> surface_text (varchar 200 NOT NULL)
            migrationBuilder.RenameColumn(
                name: "raw_mention_text",
                table: "script_line_bible_links",
                newName: "surface_text");

            migrationBuilder.AlterColumn<string>(
                name: "surface_text",
                table: "script_line_bible_links",
                type: "varchar(200)",
                nullable: true);

            // Điền surface_text từ tên mục BibleEntry khi null hoặc rỗng
            migrationBuilder.Sql(@"
                UPDATE script_line_bible_links
                SET surface_text = b.name
                FROM bible_entries b
                WHERE script_line_bible_links.bible_entry_id = b.id
                  AND (script_line_bible_links.surface_text IS NULL OR trim(script_line_bible_links.surface_text) = '');

                UPDATE script_line_bible_links
                SET surface_text = 'mention'
                WHERE surface_text IS NULL OR trim(surface_text) = '';
            ");

            migrationBuilder.AlterColumn<string>(
                name: "surface_text",
                table: "script_line_bible_links",
                type: "varchar(200)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "supersedes_revision_id",
                table: "bible_entry_revisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "aliases",
                table: "bible_entries",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<bool>(
                name: "is_crowd",
                table: "bible_entries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "bible_entry_names",
                columns: table => new
                {
                    series_bible_id = table.Column<Guid>(type: "uuid", nullable: false),
                    normalized_name = table.Column<string>(type: "varchar(200)", nullable: false),
                    bible_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bible_entry_names", x => new { x.series_bible_id, x.normalized_name });
                    table.ForeignKey(
                        name: "FK_bible_entry_names_bible_entries_bible_entry_id",
                        column: x => x.bible_entry_id,
                        principalTable: "bible_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_bible_entry_names_series_bibles_series_bible_id",
                        column: x => x.series_bible_id,
                        principalTable: "series_bibles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // BIB-09, 9A: Điền dữ liệu từ bible_entries hiện có vào bible_entry_names
            migrationBuilder.Sql(@"
                INSERT INTO bible_entry_names (series_bible_id, normalized_name, bible_entry_id, is_primary)
                SELECT series_bible_id, lower(trim(regexp_replace(name, '\s+', ' ', 'g'))), id, true
                FROM bible_entries
                WHERE is_active = true
                ON CONFLICT (series_bible_id, normalized_name) DO NOTHING;
            ");

            migrationBuilder.CreateTable(
                name: "chapter_bible_bindings",
                columns: table => new
                {
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bible_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bound_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source = table.Column<string>(type: "varchar(16)", nullable: false),
                    chosen_by_writer = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_crowd_at_bind = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    bound_by = table.Column<Guid>(type: "uuid", nullable: true),
                    bound_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chapter_bible_bindings", x => new { x.chapter_id, x.bible_entry_id });
                    table.ForeignKey(
                        name: "FK_chapter_bible_bindings_bible_entries_bible_entry_id",
                        column: x => x.bible_entry_id,
                        principalTable: "bible_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_chapter_bible_bindings_bible_entry_revisions_bound_revision~",
                        column: x => x.bound_revision_id,
                        principalTable: "bible_entry_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_chapter_bible_bindings_chapters_chapter_id",
                        column: x => x.chapter_id,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_chapter_bible_bindings_users_bound_by",
                        column: x => x.bound_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "panel_bible_variants",
                columns: table => new
                {
                    panel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bible_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    set_by = table.Column<Guid>(type: "uuid", nullable: false),
                    set_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_panel_bible_variants", x => new { x.panel_id, x.bible_entry_id });
                    table.ForeignKey(
                        name: "FK_panel_bible_variants_bible_entries_bible_entry_id",
                        column: x => x.bible_entry_id,
                        principalTable: "bible_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_panel_bible_variants_bible_entry_revisions_revision_id",
                        column: x => x.revision_id,
                        principalTable: "bible_entry_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_panel_bible_variants_panels_panel_id",
                        column: x => x.panel_id,
                        principalTable: "panels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_panel_bible_variants_users_set_by",
                        column: x => x.set_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bible_entry_revisions_supersedes_revision_id",
                table: "bible_entry_revisions",
                column: "supersedes_revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_bible_entry_names_entry",
                table: "bible_entry_names",
                column: "bible_entry_id");

            migrationBuilder.CreateIndex(
                name: "IX_chapter_bible_bindings_bound_by",
                table: "chapter_bible_bindings",
                column: "bound_by");

            migrationBuilder.CreateIndex(
                name: "ix_chapter_bible_bindings_entry",
                table: "chapter_bible_bindings",
                column: "bible_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_chapter_bible_bindings_revision",
                table: "chapter_bible_bindings",
                column: "bound_revision_id");

            migrationBuilder.CreateIndex(
                name: "IX_panel_bible_variants_bible_entry_id",
                table: "panel_bible_variants",
                column: "bible_entry_id");

            migrationBuilder.CreateIndex(
                name: "IX_panel_bible_variants_revision_id",
                table: "panel_bible_variants",
                column: "revision_id");

            migrationBuilder.CreateIndex(
                name: "IX_panel_bible_variants_set_by",
                table: "panel_bible_variants",
                column: "set_by");

            migrationBuilder.AddForeignKey(
                name: "FK_bible_entry_revisions_bible_entry_revisions_supersedes_revi~",
                table: "bible_entry_revisions",
                column: "supersedes_revision_id",
                principalTable: "bible_entry_revisions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_bible_entry_revisions_bible_entry_revisions_supersedes_revi~",
                table: "bible_entry_revisions");

            migrationBuilder.DropTable(
                name: "bible_entry_names");

            migrationBuilder.DropTable(
                name: "chapter_bible_bindings");

            migrationBuilder.DropTable(
                name: "panel_bible_variants");

            migrationBuilder.DropIndex(
                name: "ix_bible_entry_revisions_supersedes_revision_id",
                table: "bible_entry_revisions");

            migrationBuilder.AlterColumn<string>(
                name: "surface_text",
                table: "script_line_bible_links",
                type: "varchar(100)",
                nullable: true);

            migrationBuilder.RenameColumn(
                name: "surface_text",
                table: "script_line_bible_links",
                newName: "raw_mention_text");

            migrationBuilder.DropColumn(
                name: "supersedes_revision_id",
                table: "bible_entry_revisions");

            migrationBuilder.DropColumn(
                name: "aliases",
                table: "bible_entries");

            migrationBuilder.DropColumn(
                name: "is_crowd",
                table: "bible_entries");
        }
    }
}
