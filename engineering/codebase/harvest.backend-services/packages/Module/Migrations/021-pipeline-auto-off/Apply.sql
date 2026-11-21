-- 021-pipeline-auto-off: a pipeline's own failed-run threshold, where its failure count starts, and the record of the turn-off that its schedules last had (docs: engineering/deployment/deployment.md § Provisioning Haven on a target).
ALTER TABLE harvest_pipelines
    ADD COLUMN IF NOT EXISTS auto_off_after integer NULL
        CONSTRAINT ck_harvest_pipelines_auto_off_after CHECK (auto_off_after BETWEEN 0 AND 1000),
    ADD COLUMN IF NOT EXISTS failures_since timestamptz NULL,
    ADD COLUMN IF NOT EXISTS auto_off_at timestamptz NULL,
    ADD COLUMN IF NOT EXISTS auto_off_run_id uuid NULL,
    ADD COLUMN IF NOT EXISTS auto_off_failures integer NULL,
    ADD COLUMN IF NOT EXISTS auto_off_reason text NULL;
-- Runs from before this release never count.
UPDATE harvest_pipelines SET failures_since = now() WHERE failures_since IS NULL;
