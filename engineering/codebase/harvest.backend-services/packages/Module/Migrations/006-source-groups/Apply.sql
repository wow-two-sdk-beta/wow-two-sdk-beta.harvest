-- 006-source-groups — the console's Sources area: publisher groups of sources, a display name per source, and the
-- revision log the post resolve writes. Posts that existed before this migration have no revision rows.

-- A group of sources in one container, such as a publisher whose channels or branches are its sources.
CREATE TABLE harvest_source_groups (
    id           uuid        NOT NULL,
    container_id uuid        NOT NULL,
    name         text        NOT NULL,
    description  text        NULL,
    created_at   timestamptz NOT NULL,

    CONSTRAINT pk_harvest_source_groups PRIMARY KEY (id),
    CONSTRAINT ux_harvest_source_groups_container_id UNIQUE (container_id, id),
    CONSTRAINT fk_harvest_source_groups_container FOREIGN KEY (container_id) REFERENCES harvest_containers (id),
    CONSTRAINT ck_harvest_source_groups_name CHECK (char_length(name) BETWEEN 1 AND 200),
    CONSTRAINT ck_harvest_source_groups_description CHECK (description IS NULL OR char_length(description) <= 1000)
);

-- Names are unique per container whatever their case, so declaring a group twice returns the first.
CREATE UNIQUE INDEX ux_harvest_source_groups_name ON harvest_source_groups (container_id, lower(name));

-- The group references its own container, so a source never joins another container's group.
ALTER TABLE harvest_sources
    ADD COLUMN group_id     uuid NULL,
    ADD COLUMN display_name text NULL,
    ADD CONSTRAINT fk_harvest_sources_group FOREIGN KEY (container_id, group_id)
        REFERENCES harvest_source_groups (container_id, id),
    ADD CONSTRAINT ck_harvest_sources_display_name CHECK (display_name IS NULL OR char_length(display_name) BETWEEN 1 AND 200);

-- The overview counts each source's links first seen per day.
CREATE INDEX ix_harvest_post_links_first_seen ON harvest_post_links (source_id, first_seen_at);

-- One row each time a post is created (`changed = {created}`) or its revision rises; `changed` names the top-level
-- `base` fields whose JSON differs from the previous `base`, never `extraction`, `fieldSources` or `flags`.
CREATE TABLE harvest_post_revisions (
    container_id uuid        NOT NULL,
    post_id      uuid        NOT NULL,
    revision     integer     NOT NULL,
    at           timestamptz NOT NULL,
    changed      text[]      NOT NULL DEFAULT '{}',

    CONSTRAINT pk_harvest_post_revisions PRIMARY KEY (post_id, revision),
    CONSTRAINT fk_harvest_post_revisions_post FOREIGN KEY (container_id, post_id)
        REFERENCES harvest_posts (container_id, id) ON DELETE CASCADE
);
