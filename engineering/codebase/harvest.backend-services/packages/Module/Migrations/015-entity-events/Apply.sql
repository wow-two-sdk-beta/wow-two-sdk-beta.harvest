-- The entity change feed (scfd-entity-model #8, build plan step D): every change resolution or an operator makes to an
-- entity appends one row in the same transaction — created, an occurrence added, confirmed, predicted, removed or
-- changed (dates, status, place), the entity changed (status, cadence, price), merged or split. A container's products
-- pull it beside the post feed; the cursor is (tx, id) read below the snapshot horizon, as the subscription pull reads
-- its log, so a row committed late is never skipped. Rows hold names, dates, statuses and places, never post text.
-- Subscription delivery does not read it; the selector routes it per subscription later.
CREATE TABLE IF NOT EXISTS harvest_entity_events (
    id            bigint      GENERATED ALWAYS AS IDENTITY,
    tx            xid8        NOT NULL DEFAULT pg_current_xact_id(),
    event_id      uuid        NOT NULL,
    container_id  uuid        NOT NULL,
    entity_id     uuid        NOT NULL,
    entity_type   text        NOT NULL,
    domain        text        NULL,
    type          text        NOT NULL,
    occurrence_id uuid        NULL,
    post_id       uuid        NULL,
    occurred_at   timestamptz NOT NULL,
    payload       jsonb       NOT NULL DEFAULT '{}',

    CONSTRAINT pk_harvest_entity_events PRIMARY KEY (id),
    CONSTRAINT ux_harvest_entity_events_event_id UNIQUE (event_id),
    CONSTRAINT fk_harvest_entity_events_entity FOREIGN KEY (container_id, entity_id) REFERENCES harvest_entities (container_id, id) ON DELETE CASCADE,
    CONSTRAINT ck_harvest_entity_events_entity_type CHECK (entity_type IN ('event', 'listing')),
    CONSTRAINT ck_harvest_entity_events_type CHECK (type IN ('entity.created', 'entity.changed', 'entity.merged', 'entity.split',
        'occurrence.added', 'occurrence.changed', 'occurrence.confirmed', 'occurrence.predicted', 'occurrence.removed')),
    CONSTRAINT ck_harvest_entity_events_payload CHECK (jsonb_typeof(payload) = 'object')
);

CREATE INDEX IF NOT EXISTS ix_harvest_entity_events_cursor ON harvest_entity_events (container_id, tx, id);
CREATE INDEX IF NOT EXISTS ix_harvest_entity_events_entity ON harvest_entity_events (entity_id, id);
