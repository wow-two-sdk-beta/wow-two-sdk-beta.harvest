-- Older code reads ai_allowed only: a manual source reads as not allowed, so nothing queues for it.
UPDATE harvest_sources SET ai_allowed = (model_processing = 'auto');
ALTER TABLE harvest_sources DROP CONSTRAINT IF EXISTS ck_harvest_sources_model_processing;
ALTER TABLE harvest_sources ALTER COLUMN model_processing SET DEFAULT 'auto';
ALTER TABLE harvest_sources DROP COLUMN IF EXISTS model_processing;
