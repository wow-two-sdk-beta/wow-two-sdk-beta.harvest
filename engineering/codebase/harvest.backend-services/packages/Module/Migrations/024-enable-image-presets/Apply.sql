ALTER TABLE harvest_source_image_policy
    ALTER COLUMN post_presets SET DEFAULT ARRAY['s1', 's2', 's3', 's4', 's5']::text[];

UPDATE harvest_source_image_policy
SET post_presets = DEFAULT
WHERE post_presets = ARRAY['s3']::text[];
