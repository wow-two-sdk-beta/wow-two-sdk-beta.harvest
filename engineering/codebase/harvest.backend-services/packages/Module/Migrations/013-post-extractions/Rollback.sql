-- Older code reads no extraction answers and queues only post-tags.
DELETE FROM harvest_post_work WHERE kind IN ('extract-event', 'extract-listing');
ALTER TABLE harvest_post_work DROP CONSTRAINT IF EXISTS ck_harvest_post_work_kind;
ALTER TABLE harvest_post_work ADD CONSTRAINT ck_harvest_post_work_kind CHECK (kind IN ('post-tags'));
DROP TABLE IF EXISTS harvest_post_extractions;
