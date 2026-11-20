-- @no-transaction
-- 019-source-catalog: the instance's choice of which registered sources it ever uses (docs: engineering/development/source-catalog.md).
-- harvest_catalog_sources holds one row per catalog entry an instance has decided on; harvest_sources.catalog_key links a source
-- row to its entry. A source is off when its entry's row says turned_on = false; no row means no decision.
--
--   - expand-only: a new table and a new nullable column, nothing existing changes
--   - the file runs outside a transaction (`@no-transaction`), as CREATE INDEX CONCURRENTLY requires, so every statement
--     can run again: a rerun after an interrupted run finishes the rest
--   - the index is dropped first, then built: an interrupted build leaves an invalid index that CREATE INDEX IF NOT
--     EXISTS would keep

CREATE TABLE IF NOT EXISTS harvest_catalog_sources (
    key        text        NOT NULL,
    turned_on  boolean     NOT NULL,
    changed_at timestamptz NOT NULL DEFAULT now(),
    changed_by text        NOT NULL,

    CONSTRAINT pk_harvest_catalog_sources PRIMARY KEY (key),
    CONSTRAINT ck_harvest_catalog_sources_key CHECK (char_length(key) BETWEEN 1 AND 200),
    CONSTRAINT ck_harvest_catalog_sources_changed_by CHECK (char_length(changed_by) BETWEEN 1 AND 200)
);

ALTER TABLE harvest_sources ADD COLUMN IF NOT EXISTS catalog_key text NULL;

DROP INDEX CONCURRENTLY IF EXISTS ix_harvest_sources_catalog_key;
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_harvest_sources_catalog_key ON harvest_sources (catalog_key) WHERE catalog_key IS NOT NULL;
