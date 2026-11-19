-- Layer 2 extraction (scfd-entity-model build plan step B). One answer per post, content and use case: content_hash is
-- the SHA-256 of the post's title and text, the same key its done post-tags answer has, so a revision that only writes
-- tags back never queues extraction again. answer holds the validated canonical shape (extract-event or
-- extract-listing) with verbatim evidence quotes; the post's base is never written. The entity layer (step C) reads the
-- done rows. A failed or unavailable answer keeps its status and no answer, and never replaces a done one.
CREATE TABLE IF NOT EXISTS harvest_post_extractions (
    post_id        uuid        NOT NULL,
    content_hash   bytea       NOT NULL,
    use_case       text        NOT NULL,
    container_id   uuid        NOT NULL,
    revision       integer     NOT NULL,
    prompt_version integer     NULL,
    prompt_hash    text        NULL,
    inference_id   uuid        NULL,
    model          text        NULL,
    role           text        NULL,
    answer         jsonb       NULL,
    flags          jsonb       NOT NULL DEFAULT '[]',
    status         text        NOT NULL,
    error          text        NULL,
    attempts       integer     NOT NULL DEFAULT 1,
    created_at     timestamptz NOT NULL,
    updated_at     timestamptz NOT NULL,

    CONSTRAINT pk_harvest_post_extractions PRIMARY KEY (post_id, content_hash, use_case),
    CONSTRAINT fk_harvest_post_extractions_post FOREIGN KEY (container_id, post_id) REFERENCES harvest_posts (container_id, id) ON DELETE CASCADE,
    CONSTRAINT ck_harvest_post_extractions_hash CHECK (octet_length(content_hash) = 32),
    CONSTRAINT ck_harvest_post_extractions_use_case CHECK (use_case IN ('extract-event', 'extract-listing')),
    CONSTRAINT ck_harvest_post_extractions_status CHECK (status IN ('done', 'failed', 'unavailable')),
    CONSTRAINT ck_harvest_post_extractions_role CHECK (role IS NULL OR role IN ('announcement', 'update', 'report', 'mention', 'unsure')),
    CONSTRAINT ck_harvest_post_extractions_done CHECK (status <> 'done' OR (answer IS NOT NULL AND role IS NOT NULL)),
    CONSTRAINT ck_harvest_post_extractions_answer CHECK (answer IS NULL OR jsonb_typeof(answer) = 'object'),
    CONSTRAINT ck_harvest_post_extractions_flags CHECK (jsonb_typeof(flags) = 'array'),
    CONSTRAINT ck_harvest_post_extractions_attempts CHECK (attempts >= 1)
);

CREATE INDEX IF NOT EXISTS ix_harvest_post_extractions_done ON harvest_post_extractions (post_id, use_case, updated_at DESC) WHERE status = 'done';
CREATE INDEX IF NOT EXISTS ix_harvest_post_extractions_updated ON harvest_post_extractions (updated_at);

-- The extraction queues in harvest_post_work under its use cases as kinds, beside post-tags.
ALTER TABLE harvest_post_work DROP CONSTRAINT IF EXISTS ck_harvest_post_work_kind;
ALTER TABLE harvest_post_work ADD CONSTRAINT ck_harvest_post_work_kind CHECK (kind IN ('post-tags', 'extract-event', 'extract-listing')) NOT VALID;
ALTER TABLE harvest_post_work VALIDATE CONSTRAINT ck_harvest_post_work_kind;
