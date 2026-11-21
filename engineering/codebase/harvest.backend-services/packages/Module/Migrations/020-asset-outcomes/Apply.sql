-- 020-asset-outcomes: each image copy's last attempt result, when it finished and the bytes of a copy, for the `assets report` host verb (docs: engineering/deployment/deployment.md § Private source media).
ALTER TABLE harvest_source_assets
    ADD COLUMN IF NOT EXISTS bytes bigint NULL CONSTRAINT ck_harvest_source_assets_bytes CHECK (bytes >= 0),
    ADD COLUMN IF NOT EXISTS outcome text NULL,
    ADD COLUMN IF NOT EXISTS finished_at timestamptz NULL;
