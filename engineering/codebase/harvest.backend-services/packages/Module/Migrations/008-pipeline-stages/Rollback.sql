DROP TABLE harvest_step_attempts;
DROP TABLE harvest_processing_results;
DROP TABLE harvest_catalog_pages;
DROP TABLE harvest_discovery_scopes;
DROP TABLE harvest_discoveries;

DELETE FROM harvest_work_item WHERE run_id IN (SELECT id FROM harvest_runs WHERE kind IN ('harvest','process'));
DELETE FROM harvest_runs WHERE kind IN ('harvest','process');
DELETE FROM harvest_schedules WHERE kind IN ('harvest','process');

-- Older code runs every discover row as legacy discovery: drop the staged schedules and the staged runs still pending.
DELETE FROM harvest_enumerations WHERE run_id IN (
    SELECT id FROM harvest_runs WHERE kind = 'discover' AND jsonb_typeof(frozen -> 'stage') = 'object'
      AND status IN ('queued','running','paused'));
DELETE FROM harvest_runs WHERE kind = 'discover' AND jsonb_typeof(frozen -> 'stage') = 'object'
    AND status IN ('queued','running','paused');
DELETE FROM harvest_schedules WHERE kind = 'discover' AND stage_options IS NOT NULL;

ALTER TABLE harvest_runs
    DROP CONSTRAINT ck_harvest_runs_kind,
    ADD CONSTRAINT ck_harvest_runs_kind
        CHECK (kind IN ('capture','discover','refresh','sweep','check','metrics','link','intake')) NOT VALID;

ALTER TABLE harvest_schedules
    DROP COLUMN stage_options,
    DROP CONSTRAINT ck_harvest_schedules_kind,
    ADD CONSTRAINT ck_harvest_schedules_kind
        CHECK (kind IN ('capture','discover','refresh','check','metrics')) NOT VALID;
