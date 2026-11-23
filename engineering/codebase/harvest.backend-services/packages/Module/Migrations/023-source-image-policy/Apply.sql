CREATE TABLE harvest_source_image_policy (
    singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton),
    post_presets text[] NOT NULL DEFAULT ARRAY['s3']::text[]
        CHECK (cardinality(post_presets) <= 5 AND post_presets <@ ARRAY['s1', 's2', 's3', 's4', 's5']::text[]),
    collect_after timestamptz NOT NULL DEFAULT now()
);

INSERT INTO harvest_source_image_policy DEFAULT VALUES;
