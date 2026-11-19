-- Operator re-runs of the AI passes (post-tags, extract-event, extract-listing): one row per run records its scope, filters
-- and counts; the work it queues carries the run id in harvest_post_work, so its progress reads from the queue. A row an
-- operator named by post id is explicit: it may run under a source in 'manual' mode, and the work it causes downstream
-- (extraction after tags) stays explicit. Rows hold ids, counts and filters, never post text.
CREATE TABLE IF NOT EXISTS harvest_processing_runs (
    id            uuid        NOT NULL,
    use_cases     text[]      NOT NULL,
    scope         text        NOT NULL,
    source_ids    uuid[]      NOT NULL DEFAULT '{}',
    post_ids      uuid[]      NOT NULL DEFAULT '{}',
    since         timestamptz NULL,
    prompt_before integer     NULL,
    counts        jsonb       NOT NULL DEFAULT '{}',
    requested_by  text        NULL,
    created_at    timestamptz NOT NULL,

    CONSTRAINT pk_harvest_processing_runs PRIMARY KEY (id),
    CONSTRAINT ck_harvest_processing_runs_scope CHECK (scope IN ('posts', 'sources', 'all')),
    CONSTRAINT ck_harvest_processing_runs_use_cases CHECK (cardinality(use_cases) > 0
        AND use_cases <@ ARRAY['post-tags', 'extract-event', 'extract-listing']::text[]),
    CONSTRAINT ck_harvest_processing_runs_prompt_before CHECK (prompt_before IS NULL OR prompt_before > 0),
    CONSTRAINT ck_harvest_processing_runs_counts CHECK (jsonb_typeof(counts) = 'object')
);

CREATE INDEX IF NOT EXISTS ix_harvest_processing_runs_created ON harvest_processing_runs (created_at);

ALTER TABLE harvest_post_work ADD COLUMN IF NOT EXISTS run_id uuid NULL;
ALTER TABLE harvest_post_work ADD COLUMN IF NOT EXISTS explicit boolean NOT NULL DEFAULT false;
