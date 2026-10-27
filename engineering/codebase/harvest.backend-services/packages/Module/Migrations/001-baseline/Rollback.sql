-- Inverts 001-baseline — drops every Harvest table with its rows, the capture partitions and their function.
-- Dev/test recovery only. pg_trgm stays: other schemas in the database may use it, and 001 re-creates it if missing.

DROP TABLE IF EXISTS harvest_subscription_event;
DROP TABLE IF EXISTS harvest_subscriptions;
DROP TABLE IF EXISTS harvest_link_check_log;
DROP TABLE IF EXISTS harvest_link_events;
DROP TABLE IF EXISTS harvest_link_readings;
DROP TABLE IF EXISTS harvest_link_schedule;
DROP TABLE IF EXISTS harvest_post_links;
DROP TABLE IF EXISTS harvest_post_texts;
DROP TABLE IF EXISTS harvest_posts;
DROP FUNCTION IF EXISTS harvest_ensure_capture_partitions(integer);
DROP TABLE IF EXISTS harvest_captures;
DROP TABLE IF EXISTS harvest_enumerations;
DROP TABLE IF EXISTS harvest_usage;
DROP TABLE IF EXISTS harvest_host_lane;
DROP TABLE IF EXISTS harvest_host_state;
DROP TABLE IF EXISTS harvest_work_item;
DROP TABLE IF EXISTS harvest_runs;
DROP TABLE IF EXISTS harvest_schedules;
DROP TABLE IF EXISTS harvest_pipelines;
DROP TABLE IF EXISTS harvest_source_scopes;
DROP TABLE IF EXISTS harvest_sources;
DROP TABLE IF EXISTS harvest_host_policies;
DROP TABLE IF EXISTS harvest_check_policies;
DROP TABLE IF EXISTS harvest_retention_policies;
DROP TABLE IF EXISTS harvest_containers;
DROP TABLE IF EXISTS harvest_api_keys;
DROP TABLE IF EXISTS harvest_tenants;
