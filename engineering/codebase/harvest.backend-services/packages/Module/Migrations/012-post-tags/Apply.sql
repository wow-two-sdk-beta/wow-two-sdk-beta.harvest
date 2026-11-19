-- Layer 1 post-tags (scfd-entity-model build plan step A). One AI tagging answer per post and content: content_hash is
-- the SHA-256 of the post's title and text, so a revision that only writes tags back never queues tagging again. A
-- failed or unavailable answer keeps its status and writes no tags; done answers become a post revision.
CREATE TABLE IF NOT EXISTS harvest_post_tags (
    post_id        uuid         NOT NULL,
    content_hash   bytea        NOT NULL,
    container_id   uuid         NOT NULL,
    revision       integer      NOT NULL,
    prompt_version integer      NULL,
    prompt_hash    text         NULL,
    inference_id   uuid         NULL,
    model          text         NULL,
    entity_type    text         NULL,
    domains        jsonb        NOT NULL DEFAULT '[]',
    topics         text[]       NOT NULL DEFAULT '{}',
    about          text         NULL,
    timeframe      text         NULL,
    items          integer      NULL,
    language       text         NULL,
    confidence     numeric(4,3) NULL,
    title_en       text         NULL,
    flags          jsonb        NOT NULL DEFAULT '[]',
    status         text         NOT NULL,
    error          text         NULL,
    created_at     timestamptz  NOT NULL,

    CONSTRAINT pk_harvest_post_tags PRIMARY KEY (post_id, content_hash),
    CONSTRAINT fk_harvest_post_tags_post FOREIGN KEY (container_id, post_id) REFERENCES harvest_posts (container_id, id) ON DELETE CASCADE,
    CONSTRAINT ck_harvest_post_tags_hash CHECK (octet_length(content_hash) = 32),
    CONSTRAINT ck_harvest_post_tags_status CHECK (status IN ('done', 'failed', 'unavailable')),
    CONSTRAINT ck_harvest_post_tags_entity_type CHECK (entity_type IS NULL OR entity_type IN ('event', 'listing', 'none', 'unsure')),
    CONSTRAINT ck_harvest_post_tags_timeframe CHECK (timeframe IS NULL OR timeframe IN ('upcoming', 'ongoing', 'past', 'evergreen', 'unsure')),
    CONSTRAINT ck_harvest_post_tags_done CHECK (status <> 'done' OR (entity_type IS NOT NULL AND timeframe IS NOT NULL)),
    CONSTRAINT ck_harvest_post_tags_confidence CHECK (confidence IS NULL OR confidence BETWEEN 0 AND 1),
    CONSTRAINT ck_harvest_post_tags_items CHECK (items IS NULL OR items >= 0),
    CONSTRAINT ck_harvest_post_tags_domains CHECK (jsonb_typeof(domains) = 'array'),
    CONSTRAINT ck_harvest_post_tags_flags CHECK (jsonb_typeof(flags) = 'array')
);

CREATE INDEX IF NOT EXISTS ix_harvest_post_tags_done ON harvest_post_tags (post_id, created_at DESC) WHERE status = 'done';
CREATE INDEX IF NOT EXISTS ix_harvest_post_tags_created ON harvest_post_tags (created_at);

-- Work on one post outside any run: one row per post and kind, the content it waits to process, a retry time and a
-- lease. A post write queues post-tags when its title or text changes; a later write replaces the waiting content.
CREATE TABLE IF NOT EXISTS harvest_post_work (
    post_id      uuid        NOT NULL,
    kind         text        NOT NULL,
    container_id uuid        NOT NULL,
    content_hash bytea       NOT NULL,
    queued_at    timestamptz NOT NULL,
    not_before   timestamptz NOT NULL,
    attempts     integer     NOT NULL DEFAULT 0,
    lease_owner  text        NULL,
    lease_until  timestamptz NULL,
    last_error   text        NULL,

    CONSTRAINT pk_harvest_post_work PRIMARY KEY (post_id, kind),
    CONSTRAINT fk_harvest_post_work_post FOREIGN KEY (container_id, post_id) REFERENCES harvest_posts (container_id, id) ON DELETE CASCADE,
    CONSTRAINT ck_harvest_post_work_kind CHECK (kind IN ('post-tags')),
    CONSTRAINT ck_harvest_post_work_hash CHECK (octet_length(content_hash) = 32),
    CONSTRAINT ck_harvest_post_work_attempts CHECK (attempts >= 0),
    CONSTRAINT ck_harvest_post_work_lease CHECK ((lease_owner IS NULL) = (lease_until IS NULL))
);

CREATE INDEX IF NOT EXISTS ix_harvest_post_work_due ON harvest_post_work (kind, not_before);
