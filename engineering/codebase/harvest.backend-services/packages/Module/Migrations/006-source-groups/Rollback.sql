-- Inverts 006-source-groups. Dev/test recovery only: groups, display names and revision rows are dropped.
DROP TABLE harvest_post_revisions;
DROP INDEX ix_harvest_post_links_first_seen;
ALTER TABLE harvest_sources
    DROP CONSTRAINT ck_harvest_sources_display_name,
    DROP CONSTRAINT fk_harvest_sources_group,
    DROP COLUMN display_name,
    DROP COLUMN group_id;
DROP TABLE harvest_source_groups;
