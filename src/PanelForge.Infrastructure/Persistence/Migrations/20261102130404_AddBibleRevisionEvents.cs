using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBibleRevisionEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "concept_image_url",
                table: "bible_entry_revisions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "bible_revision_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bible_entry_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "varchar(30)", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    comment = table.Column<string>(type: "varchar(1000)", nullable: true),
                    image_url = table.Column<string>(type: "text", nullable: true),
                    event_hash = table.Column<string>(type: "varchar(64)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bible_revision_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_bible_revision_events_bible_entry_revisions_bible_entry_rev~",
                        column: x => x.bible_entry_revision_id,
                        principalTable: "bible_entry_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bible_revision_events_revision_created",
                table: "bible_revision_events",
                columns: new[] { "bible_entry_revision_id", "created_at" });

            // Dữ liệu cũ giữ nguyên (không gộp/xoá phiên bản nào, BR-01); chỉ BỔ SUNG dòng thời gian cho các phiên bản đã có.
            // Mốc thời gian của duyệt/yêu cầu sửa cũ là updated_at (lúc đó chưa lưu lịch sử), nên chỉ gần đúng.
            migrationBuilder.Sql("""
                UPDATE bible_entry_revisions
                SET concept_image_url = NULLIF(snapshot_json::jsonb ->> 'ReferenceImageUrl', '')
                WHERE artist_user_id IS NOT NULL AND concept_image_url IS NULL;

                WITH src AS (
                    SELECT r.id AS rev_id, 'Created' AS t, r.author_id AS actor, r.summary AS cmt, NULL::text AS img, r.created_at AS at
                    FROM bible_entry_revisions r
                    WHERE r.artist_user_id IS NULL OR r.artist_user_id IS DISTINCT FROM r.author_id
                    UNION ALL
                    SELECT r.id, 'ConceptSubmitted', r.artist_user_id, r.summary, r.concept_image_url, r.created_at
                    FROM bible_entry_revisions r
                    WHERE r.artist_user_id IS NOT NULL
                    UNION ALL
                    SELECT r.id, 'ReworkRequested', NULL, r.rework_feedback, r.concept_image_url, COALESCE(r.updated_at, r.created_at)
                    FROM bible_entry_revisions r WHERE r.review_status = 'ReworkRequired'
                    UNION ALL
                    SELECT r.id, 'Endorsed', NULL, NULL, r.concept_image_url, COALESCE(r.updated_at, r.created_at)
                    FROM bible_entry_revisions r WHERE r.review_status IN ('WriterEndorsed', 'CanonLocked')
                    UNION ALL
                    SELECT r.id, 'Locked', r.locked_by_id, NULL, r.concept_image_url, COALESCE(r.locked_at, r.updated_at, r.created_at)
                    FROM bible_entry_revisions r WHERE r.locked_at IS NOT NULL OR r.review_status = 'CanonLocked'
                )
                INSERT INTO bible_revision_events
                    (id, bible_entry_revision_id, event_type, actor_id, comment, image_url, event_hash, created_at, created_by, is_deleted)
                SELECT gen_random_uuid(), rev_id, t, actor, LEFT(cmt, 1000), img,
                       UPPER(encode(sha256(convert_to(concat_ws('|', rev_id, t, actor, cmt, img, at), 'UTF8')), 'hex')),
                       at, 'migration-backfill', false
                FROM src
                WHERE NOT EXISTS (SELECT 1 FROM bible_revision_events e WHERE e.bible_entry_revision_id = src.rev_id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bible_revision_events");

            migrationBuilder.DropColumn(
                name: "concept_image_url",
                table: "bible_entry_revisions");
        }
    }
}
