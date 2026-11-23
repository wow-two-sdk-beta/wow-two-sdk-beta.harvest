ALTER TABLE harvest_source_image_policy
    ALTER COLUMN post_presets SET DEFAULT ARRAY['s3']::text[];
