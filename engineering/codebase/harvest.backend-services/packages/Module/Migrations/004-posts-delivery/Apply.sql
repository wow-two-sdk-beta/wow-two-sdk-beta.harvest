-- Immutable local capture bodies keep the generic module useful without a Files integration.
ALTER TABLE harvest_captures ADD COLUMN body_inline bytea NULL;

-- Delivery payloads describe the event's revision even when the post has advanced before the next pull.
ALTER TABLE harvest_subscription_event ADD COLUMN payload jsonb NULL;
CREATE UNIQUE INDEX ux_harvest_subscription_item_type ON harvest_subscription_event(subscription_id,item_id,type);

CREATE TABLE harvest_post_merges (
    id uuid NOT NULL PRIMARY KEY,
    container_id uuid NOT NULL REFERENCES harvest_containers(id),
    survivor_id uuid NOT NULL,
    loser_id uuid NOT NULL,
    link_ids uuid[] NOT NULL,
    created_at timestamptz NOT NULL,
    undone_at timestamptz NULL,
    FOREIGN KEY (container_id, survivor_id) REFERENCES harvest_posts(container_id,id),
    FOREIGN KEY (container_id, loser_id) REFERENCES harvest_posts(container_id,id),
    CHECK (survivor_id <> loser_id)
);

CREATE TABLE harvest_post_keep_apart (
    container_id uuid NOT NULL,
    left_post_id uuid NOT NULL,
    right_post_id uuid NOT NULL,
    created_at timestamptz NOT NULL,
    PRIMARY KEY (container_id,left_post_id,right_post_id),
    FOREIGN KEY (container_id,left_post_id) REFERENCES harvest_posts(container_id,id),
    FOREIGN KEY (container_id,right_post_id) REFERENCES harvest_posts(container_id,id),
    CHECK (left_post_id < right_post_id)
);

-- Replaying a claimed check after a worker dies must not count as fresh disappearance evidence.
CREATE UNIQUE INDEX ux_harvest_link_check_run ON harvest_link_check_log(link_id,run_id)
    WHERE run_id IS NOT NULL;
