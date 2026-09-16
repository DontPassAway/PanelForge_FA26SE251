using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PanelForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEventSourcingElements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tạo cấu trúc bảng Append-only Event Store cho Marten (Event Sourcing)
            // Bảng mt_streams: Quản lý version stream của Aggregate Root (Page)
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS mt_streams (
                    id uuid NOT NULL,
                    type varchar(500) NULL,
                    version bigint NULL,
                    timestamp timestamptz NOT NULL DEFAULT (now()),
                    snapshot jsonb NULL,
                    created timestamptz NOT NULL DEFAULT (now()),
                    tenant_id varchar(250) NOT NULL DEFAULT ('*DEFAULT*'),
                    is_archived boolean NOT NULL DEFAULT (false),
                    CONSTRAINT pk_mt_streams PRIMARY KEY (id)
                );

                -- Bảng mt_events: Append-only Event Log lưu toàn bộ Event (ElementAdded, Moved, Removed)
                CREATE TABLE IF NOT EXISTS mt_events (
                    seq_id bigserial NOT NULL,
                    id uuid NOT NULL,
                    stream_id uuid NOT NULL,
                    version bigint NOT NULL,
                    type varchar(500) NOT NULL,
                    data jsonb NOT NULL,
                    metadata jsonb NULL,
                    timestamp timestamptz NOT NULL DEFAULT (now()),
                    tenant_id varchar(250) NOT NULL DEFAULT ('*DEFAULT*'),
                    is_archived boolean NOT NULL DEFAULT (false),
                    CONSTRAINT pk_mt_events PRIMARY KEY (seq_id),
                    CONSTRAINT ak_mt_events_id UNIQUE (id),
                    CONSTRAINT ak_mt_events_stream_id_version UNIQUE (stream_id, version)
                );

                CREATE INDEX IF NOT EXISTS ix_mt_events_stream_id ON mt_events (stream_id);
                CREATE INDEX IF NOT EXISTS ix_mt_events_type ON mt_events (type);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TABLE IF EXISTS mt_events CASCADE;
                DROP TABLE IF EXISTS mt_streams CASCADE;
            ");
        }
    }
}
