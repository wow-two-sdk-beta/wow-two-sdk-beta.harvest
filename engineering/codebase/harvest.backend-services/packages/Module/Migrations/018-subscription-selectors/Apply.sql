-- @no-transaction
-- 018-subscription-selectors: subscriptions that follow a selector over Layer 1 tags and sources instead of one
-- pipeline (wow-two-ws events-integration subscription-modes #1, #8-#11, #14). Each event's verdict is
-- harvest_subscription_event.matched, a column of 001-baseline unused until now.
--
--   - the file runs outside a transaction (`@no-transaction`), as CREATE INDEX CONCURRENTLY requires, so every statement
--     can run again: a rerun after an interrupted run finishes the rest
--   - each check on an existing table is added NOT VALID, then validated in the next statement, so writes go on
--   - each index is dropped first, then built: an interrupted build leaves an invalid index that CREATE INDEX IF NOT
--     EXISTS would keep

ALTER TABLE harvest_subscriptions DROP CONSTRAINT IF EXISTS ck_harvest_subscriptions_kind;
ALTER TABLE harvest_subscriptions ADD CONSTRAINT ck_harvest_subscriptions_kind
    CHECK (kind IN ('channel', 'tag', 'keyword', 'selector')) NOT VALID;
ALTER TABLE harvest_subscriptions VALIDATE CONSTRAINT ck_harvest_subscriptions_kind;

ALTER TABLE harvest_subscriptions ADD COLUMN IF NOT EXISTS name text NULL;
ALTER TABLE harvest_subscriptions ADD COLUMN IF NOT EXISTS selector jsonb NULL;
ALTER TABLE harvest_subscriptions ADD COLUMN IF NOT EXISTS selector_version integer NULL;

ALTER TABLE harvest_subscriptions DROP CONSTRAINT IF EXISTS ck_harvest_subscriptions_name;
ALTER TABLE harvest_subscriptions ADD CONSTRAINT ck_harvest_subscriptions_name
    CHECK (name IS NULL OR name ~ '^[a-z0-9][a-z0-9-]{0,63}$') NOT VALID;
ALTER TABLE harvest_subscriptions VALIDATE CONSTRAINT ck_harvest_subscriptions_name;

ALTER TABLE harvest_subscriptions DROP CONSTRAINT IF EXISTS ck_harvest_subscriptions_selector_object;
ALTER TABLE harvest_subscriptions ADD CONSTRAINT ck_harvest_subscriptions_selector_object
    CHECK (selector IS NULL OR jsonb_typeof(selector) = 'object') NOT VALID;
ALTER TABLE harvest_subscriptions VALIDATE CONSTRAINT ck_harvest_subscriptions_selector_object;

ALTER TABLE harvest_subscriptions DROP CONSTRAINT IF EXISTS ck_harvest_subscriptions_selector;
ALTER TABLE harvest_subscriptions ADD CONSTRAINT ck_harvest_subscriptions_selector CHECK (
    (kind = 'selector') = (selector IS NOT NULL)
    AND (selector IS NULL) = (selector_version IS NULL)
    AND (selector_version IS NULL OR selector_version > 0)
    AND (kind <> 'selector' OR (name IS NOT NULL AND filter_key IS NULL))) NOT VALID;
ALTER TABLE harvest_subscriptions VALIDATE CONSTRAINT ck_harvest_subscriptions_selector;

DROP INDEX CONCURRENTLY IF EXISTS ux_harvest_subscriptions_tenant_name;
CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS ux_harvest_subscriptions_tenant_name ON harvest_subscriptions (tenant_id, name)
    WHERE name IS NOT NULL AND state <> 'archived';

DROP INDEX CONCURRENTLY IF EXISTS ix_harvest_subscriptions_selectors;
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_harvest_subscriptions_selectors ON harvest_subscriptions (container_id)
    WHERE kind = 'selector' AND state = 'active';

-- Every version a selector subscription has had, with the window its change re-judged and what that wrote.
CREATE TABLE IF NOT EXISTS harvest_subscription_selectors (
    subscription_id uuid        NOT NULL,
    version         integer     NOT NULL,
    selector        jsonb       NOT NULL,
    window_since    timestamptz NULL,
    counts          jsonb       NOT NULL DEFAULT '{}',
    changed_by      text        NULL,
    created_at      timestamptz NOT NULL,

    CONSTRAINT pk_harvest_subscription_selectors PRIMARY KEY (subscription_id, version),
    CONSTRAINT fk_harvest_subscription_selectors_subscription FOREIGN KEY (subscription_id)
        REFERENCES harvest_subscriptions (id) ON DELETE CASCADE,
    CONSTRAINT ck_harvest_subscription_selectors_version CHECK (version > 0),
    CONSTRAINT ck_harvest_subscription_selectors_counts CHECK (jsonb_typeof(counts) = 'object'),
    CONSTRAINT ck_harvest_subscription_selectors_changed_by CHECK (changed_by IS NULL OR char_length(changed_by) <= 200)
);

ALTER TABLE harvest_subscription_event DROP CONSTRAINT IF EXISTS ck_harvest_subscription_event_matched;
ALTER TABLE harvest_subscription_event ADD CONSTRAINT ck_harvest_subscription_event_matched
    CHECK (matched IS NULL OR jsonb_typeof(matched) = 'object') NOT VALID;
ALTER TABLE harvest_subscription_event VALIDATE CONSTRAINT ck_harvest_subscription_event_matched;
