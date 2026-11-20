-- Older code knows no catalog: every source is on again, and the instance's choices are forgotten.
DROP INDEX IF EXISTS ix_harvest_sources_catalog_key;
ALTER TABLE harvest_sources DROP COLUMN IF EXISTS catalog_key;
DROP TABLE IF EXISTS harvest_catalog_sources;
