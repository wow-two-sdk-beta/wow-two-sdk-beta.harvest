-- Model processing becomes a mode per source (scfd AI operator controls): 'off' sends nothing to a model, 'manual' queues
-- nothing on its own but lets an operator run named posts or listed sources, 'auto' queues on every write as
-- ai_allowed = true did. A source that allowed AI becomes 'auto', one that did not becomes 'off', and a new row defaults
-- to 'off'. ai_allowed stays, kept true only for 'auto' by the code, so code before this migration never queues a manual
-- source.
ALTER TABLE harvest_sources ADD COLUMN IF NOT EXISTS model_processing text NOT NULL DEFAULT 'auto';

UPDATE harvest_sources SET model_processing = CASE WHEN ai_allowed THEN 'auto' ELSE 'off' END;

-- From here a row inserted without a mode starts with nothing sent to a model; the code always writes the mode.
ALTER TABLE harvest_sources ALTER COLUMN model_processing SET DEFAULT 'off';

ALTER TABLE harvest_sources DROP CONSTRAINT IF EXISTS ck_harvest_sources_model_processing;
ALTER TABLE harvest_sources ADD CONSTRAINT ck_harvest_sources_model_processing CHECK (model_processing IN ('off', 'manual', 'auto')) NOT VALID;
ALTER TABLE harvest_sources VALIDATE CONSTRAINT ck_harvest_sources_model_processing;
