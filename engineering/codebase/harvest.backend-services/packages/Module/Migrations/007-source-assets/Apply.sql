-- Private Files references for source icons and harvested post images.
CREATE TABLE harvest_source_assets (
    id uuid PRIMARY KEY,
    source_id uuid NOT NULL REFERENCES harvest_sources (id) ON DELETE CASCADE,
    kind text NOT NULL CHECK (kind IN ('favicon', 'image')),
    origin_url text NOT NULL,
    file_id uuid NULL,
    retry_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (source_id, kind, origin_url)
);

CREATE INDEX ix_harvest_source_assets_pending
    ON harvest_source_assets (retry_at, id) WHERE file_id IS NULL;

CREATE UNIQUE INDEX ux_harvest_source_assets_favicon
    ON harvest_source_assets (source_id) WHERE kind='favicon';

-- Older runs retain their reported capture count as a fallback.
ALTER TABLE harvest_runs ADD COLUMN new_posts integer CHECK (new_posts >= 0);
