-- Older code records no copy outcome or size; the copies themselves stay.
ALTER TABLE harvest_source_assets
    DROP COLUMN IF EXISTS finished_at,
    DROP COLUMN IF EXISTS outcome,
    DROP COLUMN IF EXISTS bytes;
