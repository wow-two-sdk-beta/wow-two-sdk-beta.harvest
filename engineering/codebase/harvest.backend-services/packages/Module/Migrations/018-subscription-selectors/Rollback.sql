-- Older code reads every subscription as a channel with a pipeline, so selector subscriptions leave first; their events
-- and history cascade.
DELETE FROM harvest_subscriptions WHERE kind = 'selector';
ALTER TABLE harvest_subscription_event DROP CONSTRAINT IF EXISTS ck_harvest_subscription_event_matched;
UPDATE harvest_subscription_event SET matched = NULL WHERE matched IS NOT NULL;
DROP TABLE IF EXISTS harvest_subscription_selectors;
DROP INDEX IF EXISTS ix_harvest_subscriptions_selectors;
DROP INDEX IF EXISTS ux_harvest_subscriptions_tenant_name;
ALTER TABLE harvest_subscriptions DROP CONSTRAINT IF EXISTS ck_harvest_subscriptions_selector;
ALTER TABLE harvest_subscriptions DROP CONSTRAINT IF EXISTS ck_harvest_subscriptions_selector_object;
ALTER TABLE harvest_subscriptions DROP CONSTRAINT IF EXISTS ck_harvest_subscriptions_name;
ALTER TABLE harvest_subscriptions DROP COLUMN IF EXISTS selector_version, DROP COLUMN IF EXISTS selector,
    DROP COLUMN IF EXISTS name;
ALTER TABLE harvest_subscriptions DROP CONSTRAINT IF EXISTS ck_harvest_subscriptions_kind;
ALTER TABLE harvest_subscriptions ADD CONSTRAINT ck_harvest_subscriptions_kind CHECK (kind IN ('channel', 'tag', 'keyword'));
