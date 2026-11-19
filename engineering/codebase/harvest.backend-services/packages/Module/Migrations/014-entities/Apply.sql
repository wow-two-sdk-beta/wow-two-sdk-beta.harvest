-- The entity layer (scfd-entity-model build plan step C). An entity is the real-world thing posts are about: an event,
-- one record across its editions, or a listing, one record across authors and sites. Posts bind to entities through
-- relations with a role (#3.1); dedup is the binding. Rows hold names, facts without evidence quotes, and offers;
-- never post text.
CREATE TABLE IF NOT EXISTS harvest_entities (
    id                 uuid         NOT NULL,
    container_id       uuid         NOT NULL,
    entity_type        text         NOT NULL,
    name               text         NOT NULL,
    name_key           text         NOT NULL,
    aliases            text[]       NOT NULL DEFAULT '{}',
    alias_keys         text[]       NOT NULL DEFAULT '{}',
    primary_domain     text         NULL,
    facts              jsonb        NOT NULL DEFAULT '{}',
    status             text         NOT NULL DEFAULT 'active',
    cadence            text         NOT NULL DEFAULT 'unknown',
    cadence_confidence numeric(4,3) NOT NULL DEFAULT 0,
    first_seen_at      timestamptz  NOT NULL,
    last_seen_at       timestamptz  NOT NULL,
    merged_into        uuid         NULL,
    created_at         timestamptz  NOT NULL,
    updated_at         timestamptz  NOT NULL,

    CONSTRAINT pk_harvest_entities PRIMARY KEY (id),
    CONSTRAINT ux_harvest_entities_container_id UNIQUE (container_id, id),
    CONSTRAINT fk_harvest_entities_container FOREIGN KEY (container_id) REFERENCES harvest_containers (id),
    CONSTRAINT fk_harvest_entities_merged_into FOREIGN KEY (container_id, merged_into) REFERENCES harvest_entities (container_id, id),
    CONSTRAINT ck_harvest_entities_type CHECK (entity_type IN ('event', 'listing')),
    CONSTRAINT ck_harvest_entities_name CHECK (char_length(name) BETWEEN 1 AND 300),
    CONSTRAINT ck_harvest_entities_name_key CHECK (char_length(name_key) <= 300),
    CONSTRAINT ck_harvest_entities_status CHECK (status IN ('active', 'sold', 'withdrawn')),
    CONSTRAINT ck_harvest_entities_cadence CHECK (cadence IN ('annual', 'semiannual', 'quarterly', 'monthly', 'irregular', 'unknown')),
    CONSTRAINT ck_harvest_entities_cadence_confidence CHECK (cadence_confidence BETWEEN 0 AND 1),
    CONSTRAINT ck_harvest_entities_facts CHECK (jsonb_typeof(facts) = 'object'),
    CONSTRAINT ck_harvest_entities_merge CHECK (merged_into IS NULL OR merged_into <> id)
);

CREATE INDEX IF NOT EXISTS ix_harvest_entities_name_key ON harvest_entities USING gin (name_key gin_trgm_ops);
CREATE INDEX IF NOT EXISTS ix_harvest_entities_open ON harvest_entities (container_id, entity_type, last_seen_at DESC) WHERE merged_into IS NULL;
CREATE INDEX IF NOT EXISTS ix_harvest_entities_merged_into ON harvest_entities (merged_into) WHERE merged_into IS NOT NULL;

-- One run of an event: a year's edition or an ordinal one. A predicted row is the next run the cadence expects; a real
-- one replaces it. history keeps every prior value with the post that changed it.
CREATE TABLE IF NOT EXISTS harvest_entity_occurrences (
    id         uuid        NOT NULL,
    entity_id  uuid        NOT NULL,
    label      text        NOT NULL,
    starts     date        NOT NULL,
    ends       date        NULL,
    precision  text        NOT NULL,
    certainty  text        NOT NULL,
    predicted  boolean     NOT NULL DEFAULT false,
    status     text        NOT NULL DEFAULT 'scheduled',
    place      jsonb       NULL,
    history    jsonb       NOT NULL DEFAULT '[]',
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,

    CONSTRAINT pk_harvest_entity_occurrences PRIMARY KEY (id),
    CONSTRAINT fk_harvest_entity_occurrences_entity FOREIGN KEY (entity_id) REFERENCES harvest_entities (id) ON DELETE CASCADE,
    CONSTRAINT ck_harvest_entity_occurrences_label CHECK (char_length(label) BETWEEN 1 AND 40),
    CONSTRAINT ck_harvest_entity_occurrences_dates CHECK (ends IS NULL OR ends >= starts),
    CONSTRAINT ck_harvest_entity_occurrences_precision CHECK (precision IN ('year', 'month', 'day', 'hour', 'minute', 'second')),
    CONSTRAINT ck_harvest_entity_occurrences_certainty CHECK (certainty IN ('stated', 'estimated')),
    CONSTRAINT ck_harvest_entity_occurrences_status CHECK (status IN ('scheduled', 'cancelled', 'postponed', 'held', 'unknown')),
    CONSTRAINT ck_harvest_entity_occurrences_predicted CHECK (NOT predicted OR certainty = 'estimated'),
    CONSTRAINT ck_harvest_entity_occurrences_place CHECK (place IS NULL OR jsonb_typeof(place) = 'object'),
    CONSTRAINT ck_harvest_entity_occurrences_history CHECK (jsonb_typeof(history) = 'array')
);

CREATE INDEX IF NOT EXISTS ix_harvest_entity_occurrences_entity ON harvest_entity_occurrences (entity_id, starts);

-- A post's binding to an entity: its role, what an update changed and, for a listing, the post's own offer. A digest
-- post holds one row per entity it lists. content_hash and use_case name the extraction answer it came from.
CREATE TABLE IF NOT EXISTS harvest_entity_relations (
    post_id       uuid         NOT NULL,
    entity_id     uuid         NOT NULL,
    container_id  uuid         NOT NULL,
    content_hash  bytea        NOT NULL,
    use_case      text         NOT NULL,
    occurrence_id uuid         NULL,
    role          text         NOT NULL,
    changes       text[]       NOT NULL DEFAULT '{}',
    offer         jsonb        NULL,
    confidence    numeric(4,3) NOT NULL,
    created_at    timestamptz  NOT NULL,
    updated_at    timestamptz  NOT NULL,

    CONSTRAINT pk_harvest_entity_relations PRIMARY KEY (post_id, entity_id),
    CONSTRAINT fk_harvest_entity_relations_post FOREIGN KEY (container_id, post_id) REFERENCES harvest_posts (container_id, id) ON DELETE CASCADE,
    CONSTRAINT fk_harvest_entity_relations_entity FOREIGN KEY (container_id, entity_id) REFERENCES harvest_entities (container_id, id) ON DELETE CASCADE,
    CONSTRAINT fk_harvest_entity_relations_occurrence FOREIGN KEY (occurrence_id) REFERENCES harvest_entity_occurrences (id) ON DELETE SET NULL,
    CONSTRAINT ck_harvest_entity_relations_hash CHECK (octet_length(content_hash) = 32),
    CONSTRAINT ck_harvest_entity_relations_use_case CHECK (use_case IN ('extract-event', 'extract-listing')),
    CONSTRAINT ck_harvest_entity_relations_role CHECK (role IN ('announcement', 'update', 'report', 'mention', 'unsure')),
    CONSTRAINT ck_harvest_entity_relations_changes CHECK (role = 'update' OR cardinality(changes) = 0),
    CONSTRAINT ck_harvest_entity_relations_offer CHECK (offer IS NULL OR jsonb_typeof(offer) = 'object'),
    CONSTRAINT ck_harvest_entity_relations_confidence CHECK (confidence BETWEEN 0 AND 1)
);

CREATE INDEX IF NOT EXISTS ix_harvest_entity_relations_entity ON harvest_entity_relations (entity_id, created_at);
CREATE INDEX IF NOT EXISTS ix_harvest_entity_relations_occurrence ON harvest_entity_relations (occurrence_id) WHERE occurrence_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_harvest_entity_relations_offer ON harvest_entity_relations (container_id, (offer->>'sourceId'), (offer->>'externalKey'))
    WHERE offer IS NOT NULL;

-- An operator's split: the pair is never matched or merged again.
CREATE TABLE IF NOT EXISTS harvest_entity_keep_apart (
    container_id    uuid        NOT NULL,
    left_entity_id  uuid        NOT NULL,
    right_entity_id uuid        NOT NULL,
    created_at      timestamptz NOT NULL,

    CONSTRAINT pk_harvest_entity_keep_apart PRIMARY KEY (container_id, left_entity_id, right_entity_id),
    CONSTRAINT fk_harvest_entity_keep_apart_left FOREIGN KEY (container_id, left_entity_id) REFERENCES harvest_entities (container_id, id) ON DELETE CASCADE,
    CONSTRAINT fk_harvest_entity_keep_apart_right FOREIGN KEY (container_id, right_entity_id) REFERENCES harvest_entities (container_id, id) ON DELETE CASCADE,
    CONSTRAINT ck_harvest_entity_keep_apart_order CHECK (left_entity_id < right_entity_id)
);

-- An unsure resolution (#9): a match too weak or too ambiguous to bind, kept for review with its best candidate. A
-- later resolve of the same answer that binds closes it.
CREATE TABLE IF NOT EXISTS harvest_entity_flags (
    post_id      uuid         NOT NULL,
    content_hash bytea        NOT NULL,
    use_case     text         NOT NULL,
    field        text         NOT NULL,
    container_id uuid         NOT NULL,
    reason       text         NOT NULL,
    candidate_id uuid         NULL,
    confidence   numeric(4,3) NULL,
    created_at   timestamptz  NOT NULL,
    resolved_at  timestamptz  NULL,

    CONSTRAINT pk_harvest_entity_flags PRIMARY KEY (post_id, content_hash, use_case, field),
    CONSTRAINT fk_harvest_entity_flags_post FOREIGN KEY (container_id, post_id) REFERENCES harvest_posts (container_id, id) ON DELETE CASCADE,
    CONSTRAINT fk_harvest_entity_flags_candidate FOREIGN KEY (candidate_id) REFERENCES harvest_entities (id) ON DELETE SET NULL,
    CONSTRAINT ck_harvest_entity_flags_hash CHECK (octet_length(content_hash) = 32),
    CONSTRAINT ck_harvest_entity_flags_use_case CHECK (use_case IN ('extract-event', 'extract-listing')),
    CONSTRAINT ck_harvest_entity_flags_field CHECK (field IN ('match', 'name')),
    CONSTRAINT ck_harvest_entity_flags_confidence CHECK (confidence IS NULL OR confidence BETWEEN 0 AND 1)
);

CREATE INDEX IF NOT EXISTS ix_harvest_entity_flags_open ON harvest_entity_flags (created_at) WHERE resolved_at IS NULL;

-- Resolution queues in harvest_post_work beside tagging and extraction once a done extraction lands.
ALTER TABLE harvest_post_work DROP CONSTRAINT IF EXISTS ck_harvest_post_work_kind;
ALTER TABLE harvest_post_work ADD CONSTRAINT ck_harvest_post_work_kind
    CHECK (kind IN ('post-tags', 'extract-event', 'extract-listing', 'entity-resolve')) NOT VALID;
ALTER TABLE harvest_post_work VALIDATE CONSTRAINT ck_harvest_post_work_kind;
