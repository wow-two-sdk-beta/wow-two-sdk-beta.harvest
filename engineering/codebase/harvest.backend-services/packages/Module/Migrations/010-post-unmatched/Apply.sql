ALTER TABLE harvest_subscription_event DROP CONSTRAINT ck_harvest_subscription_event_type;
ALTER TABLE harvest_subscription_event ADD CONSTRAINT ck_harvest_subscription_event_type CHECK (type IN ('post.new', 'post.changed',
    'post.extracted', 'link.state', 'pipeline.run', 'pipeline.health', 'post.merged', 'post.unmerged', 'post.purged', 'post.relisted',
    'post.unmatched'));
