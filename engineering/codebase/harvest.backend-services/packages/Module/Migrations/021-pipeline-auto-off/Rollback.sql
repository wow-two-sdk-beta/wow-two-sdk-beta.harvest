-- Older code never turns a pipeline off; schedules that were already turned off stay off.
ALTER TABLE harvest_pipelines
    DROP COLUMN IF EXISTS auto_off_reason,
    DROP COLUMN IF EXISTS auto_off_failures,
    DROP COLUMN IF EXISTS auto_off_run_id,
    DROP COLUMN IF EXISTS auto_off_at,
    DROP COLUMN IF EXISTS failures_since,
    DROP COLUMN IF EXISTS auto_off_after;
