-- Staged execution shares the existing schedule and run records. The old checks
-- already guarantee the narrower set for every existing row.
ALTER TABLE harvest_schedules
    ADD COLUMN stage_options jsonb NULL,
    DROP CONSTRAINT ck_harvest_schedules_kind,
    ADD CONSTRAINT ck_harvest_schedules_kind
        CHECK (kind IN ('capture','discover','refresh','check','metrics','harvest','process')) NOT VALID;

ALTER TABLE harvest_runs
    DROP CONSTRAINT ck_harvest_runs_kind,
    ADD CONSTRAINT ck_harvest_runs_kind
        CHECK (kind IN ('capture','discover','refresh','sweep','check','metrics','link','intake','harvest','process')) NOT VALID;

CREATE TABLE harvest_discoveries (
    id                 uuid        PRIMARY KEY,
    container_id       uuid        NOT NULL,
    source_id          uuid        NOT NULL,
    external_key       text        NOT NULL,
    url                text        NOT NULL,
    title              text        NULL,
    published_at       timestamptz NULL,
    source_modified_at timestamptz NULL,
    native_fields      jsonb       NULL,
    first_seen_at      timestamptz NOT NULL,
    last_seen_at       timestamptz NOT NULL,
    detail_state       text        NOT NULL DEFAULT 'pending',
    next_detail_at     timestamptz NOT NULL,
    revision           bigint      NOT NULL DEFAULT 1,
    link_id            uuid        NULL,
    reserved_run_id    uuid        NULL,

    UNIQUE (container_id, id),
    UNIQUE (container_id, source_id, external_key),
    FOREIGN KEY (container_id, source_id) REFERENCES harvest_sources (container_id, id),
    FOREIGN KEY (container_id, link_id) REFERENCES harvest_post_links (container_id, id),
    CHECK (detail_state IN ('pending','captured','failed','gone'))
);

CREATE INDEX ix_harvest_discoveries_due
    ON harvest_discoveries (container_id, source_id, next_detail_at, id)
    WHERE detail_state IN ('pending','failed');
CREATE INDEX ix_harvest_discoveries_published
    ON harvest_discoveries (container_id, published_at DESC, id) WHERE published_at IS NOT NULL;

CREATE TABLE harvest_discovery_scopes (
    container_id  uuid        NOT NULL,
    pipeline_id   uuid        NOT NULL,
    discovery_id  uuid        NOT NULL,
    catalog_url   text        NOT NULL,
    last_seen_at  timestamptz NOT NULL,

    PRIMARY KEY (pipeline_id, discovery_id),
    FOREIGN KEY (container_id, pipeline_id) REFERENCES harvest_pipelines (container_id, id) ON DELETE CASCADE,
    FOREIGN KEY (container_id, discovery_id) REFERENCES harvest_discoveries (container_id, id) ON DELETE CASCADE
);

CREATE INDEX ix_harvest_discovery_scopes_recent
    ON harvest_discovery_scopes (pipeline_id, last_seen_at DESC, discovery_id);

CREATE TABLE harvest_catalog_pages (
    pipeline_id   uuid        NOT NULL REFERENCES harvest_pipelines (id) ON DELETE CASCADE,
    target_key    text        NOT NULL,
    target        text        NOT NULL,
    payload       jsonb       NULL,
    entry_url     text        NOT NULL,
    queued_at     timestamptz NOT NULL,
    completed_at  timestamptz NULL,

    PRIMARY KEY (pipeline_id, target_key)
);

CREATE INDEX ix_harvest_catalog_pages_pending
    ON harvest_catalog_pages (pipeline_id, queued_at, target_key) WHERE completed_at IS NULL;

CREATE TABLE harvest_processing_results (
    capture_id     uuid        NOT NULL,
    processor      text        NOT NULL,
    container_id   uuid        NOT NULL,
    link_id        uuid        NOT NULL,
    run_id         uuid        NOT NULL,
    next_ready_at  timestamptz NOT NULL,
    processed_at   timestamptz NULL,
    result         jsonb       NULL,

    PRIMARY KEY (capture_id, processor),
    FOREIGN KEY (container_id, link_id) REFERENCES harvest_post_links (container_id, id),
    FOREIGN KEY (container_id, run_id) REFERENCES harvest_runs (container_id, id) ON DELETE CASCADE
);

CREATE INDEX ix_harvest_discoveries_link ON harvest_discoveries (link_id) WHERE link_id IS NOT NULL;

CREATE TABLE harvest_step_attempts (
    run_id         uuid        NOT NULL REFERENCES harvest_runs (id) ON DELETE CASCADE,
    work_item_id   uuid        NOT NULL REFERENCES harvest_work_item (id) ON DELETE CASCADE,
    attempt        integer     NOT NULL,
    started_at     timestamptz NOT NULL,
    finished_at    timestamptz NULL,
    outcome        text        NULL,
    message        text        NULL,
    retry_at       timestamptz NULL,
    http_status    smallint    NULL,
    discovered     integer     NOT NULL DEFAULT 0,

    PRIMARY KEY (work_item_id, attempt)
);

CREATE INDEX ix_harvest_step_attempts_run ON harvest_step_attempts (run_id, started_at, work_item_id, attempt);
