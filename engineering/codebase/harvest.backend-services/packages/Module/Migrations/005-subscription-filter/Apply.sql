ALTER TABLE harvest_subscriptions ADD COLUMN filter_key text NULL
    CONSTRAINT ck_harvest_subscription_filter_key CHECK (filter_key IS NULL OR length(filter_key) BETWEEN 1 AND 200);

CREATE INDEX ix_harvest_subscription_post_history ON harvest_subscription_event(subscription_id,post_id,id DESC)
    WHERE post_id IS NOT NULL;
