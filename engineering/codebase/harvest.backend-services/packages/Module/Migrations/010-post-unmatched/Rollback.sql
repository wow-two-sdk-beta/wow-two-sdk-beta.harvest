-- Older code knows no post.unmatched, so its rows leave the log before the type check narrows again.
DELETE FROM harvest_subscription_event WHERE type = 'post.unmatched';
ALTER TABLE harvest_subscription_event DROP CONSTRAINT ck_harvest_subscription_event_type;
ALTER TABLE harvest_subscription_event ADD CONSTRAINT ck_harvest_subscription_event_type CHECK (type IN ('post.new', 'post.changed',
    'post.extracted', 'link.state', 'pipeline.run', 'pipeline.health', 'post.merged', 'post.unmerged', 'post.purged', 'post.relisted'));
