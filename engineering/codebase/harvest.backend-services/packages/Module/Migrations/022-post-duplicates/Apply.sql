-- A copy link points directly to a reference link in the same container. Rechecking either reading
-- refreshes its memberships so a similar copy cannot join a group through another copy alone.
CREATE TABLE harvest_post_duplicates (
    container_id       uuid        NOT NULL,
    link_id            uuid        PRIMARY KEY,
    reference_link_id  uuid        NOT NULL,
    match              text        NOT NULL CHECK (match IN ('exact','similar')),
    similarity         real        NOT NULL CHECK (similarity BETWEEN 0 AND 1),
    matched_at         timestamptz NOT NULL,

    CONSTRAINT fk_harvest_duplicates_copy FOREIGN KEY (container_id, link_id)
      REFERENCES harvest_post_links (container_id, id) ON DELETE CASCADE,
    CONSTRAINT fk_harvest_duplicates_reference FOREIGN KEY (container_id, reference_link_id)
      REFERENCES harvest_post_links (container_id, id) ON DELETE CASCADE,
    CONSTRAINT ck_harvest_duplicates_distinct_links CHECK (link_id <> reference_link_id)
);

CREATE INDEX ix_harvest_duplicates_reference ON harvest_post_duplicates (container_id,reference_link_id);
CREATE INDEX ix_harvest_links_duplicate_hash ON harvest_post_links (container_id,latest_content_hash)
    WHERE retired_at IS NULL AND latest_content_hash IS NOT NULL;
