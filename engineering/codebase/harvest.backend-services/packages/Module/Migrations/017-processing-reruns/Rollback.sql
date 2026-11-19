-- Older code queues no re-runs; queued rows keep running as ordinary work.
ALTER TABLE harvest_post_work DROP COLUMN IF EXISTS explicit;
ALTER TABLE harvest_post_work DROP COLUMN IF EXISTS run_id;
DROP TABLE IF EXISTS harvest_processing_runs;
