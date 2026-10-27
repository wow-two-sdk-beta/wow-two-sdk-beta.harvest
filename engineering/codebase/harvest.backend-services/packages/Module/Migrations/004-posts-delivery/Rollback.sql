DROP INDEX ux_harvest_link_check_run;
DROP TABLE harvest_post_keep_apart;
DROP TABLE harvest_post_merges;
DROP INDEX ux_harvest_subscription_item_type;
ALTER TABLE harvest_subscription_event DROP COLUMN payload;
ALTER TABLE harvest_captures DROP COLUMN body_inline;
