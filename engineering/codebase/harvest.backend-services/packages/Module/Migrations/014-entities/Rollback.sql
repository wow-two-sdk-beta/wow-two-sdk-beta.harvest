-- Older code reads no entities and queues no resolution.
DELETE FROM harvest_post_work WHERE kind = 'entity-resolve';
ALTER TABLE harvest_post_work DROP CONSTRAINT IF EXISTS ck_harvest_post_work_kind;
ALTER TABLE harvest_post_work ADD CONSTRAINT ck_harvest_post_work_kind CHECK (kind IN ('post-tags', 'extract-event', 'extract-listing'));
DROP TABLE IF EXISTS harvest_entity_flags;
DROP TABLE IF EXISTS harvest_entity_keep_apart;
DROP TABLE IF EXISTS harvest_entity_relations;
DROP TABLE IF EXISTS harvest_entity_occurrences;
DROP TABLE IF EXISTS harvest_entities;
