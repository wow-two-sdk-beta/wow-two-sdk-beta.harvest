ALTER TABLE harvest_step_attempts ADD COLUMN operations jsonb NOT NULL DEFAULT '[]'::jsonb;
