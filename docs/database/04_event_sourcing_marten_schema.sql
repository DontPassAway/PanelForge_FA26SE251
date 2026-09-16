-- =============================================================================
-- PanelForge Manga Production Platform
-- Migration: Add Event Sourcing Schema (Marten Event Store)
-- Target Database: PostgreSQL 16+
-- =============================================================================

-- 1. Bảng mt_streams: Quản lý version stream của Aggregate Root (Page)
CREATE TABLE IF NOT EXISTS public.mt_streams (
    id          uuid         NOT NULL,
    type        varchar(500) NULL,
    version     bigint       NULL,
    timestamp   timestamptz  NOT NULL DEFAULT (now()),
    snapshot    jsonb        NULL,
    created     timestamptz  NOT NULL DEFAULT (now()),
    tenant_id   varchar(250) NOT NULL DEFAULT ('*DEFAULT*'),
    is_archived boolean      NOT NULL DEFAULT (false),
    CONSTRAINT pk_mt_streams PRIMARY KEY (id)
);

-- 2. Bảng mt_events: Append-only Event Log lưu toàn bộ Event (ElementAdded, Moved, Removed)
-- Tuyệt đối không UPDATE, không DELETE. Mọi thao tác là INSERT (APPEND) với Version tăng dần.
CREATE TABLE IF NOT EXISTS public.mt_events (
    seq_id      bigserial    NOT NULL,
    id          uuid         NOT NULL,
    stream_id   uuid         NOT NULL,
    version     bigint       NOT NULL,
    type        varchar(500) NOT NULL,
    data        jsonb        NOT NULL,
    metadata    jsonb        NULL,
    timestamp   timestamptz  NOT NULL DEFAULT (now()),
    tenant_id   varchar(250) NOT NULL DEFAULT ('*DEFAULT*'),
    is_archived boolean      NOT NULL DEFAULT (false),
    CONSTRAINT pk_mt_events PRIMARY KEY (seq_id),
    CONSTRAINT ak_mt_events_id UNIQUE (id),
    CONSTRAINT ak_mt_events_stream_id_version UNIQUE (stream_id, version)
);

-- 3. Indexes phục vụ truy vấn tối ưu theo PageId (Stream) và loại Event
CREATE INDEX IF NOT EXISTS ix_mt_events_stream_id ON public.mt_events (stream_id);
CREATE INDEX IF NOT EXISTS ix_mt_events_type ON public.mt_events (type);
CREATE INDEX IF NOT EXISTS ix_mt_events_timestamp ON public.mt_events (timestamp);
