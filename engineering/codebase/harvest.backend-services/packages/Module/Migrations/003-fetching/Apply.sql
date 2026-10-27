-- Public response validators and their bounded bodies allow crash-safe replay after a 304.
ALTER TABLE harvest_host_state ADD COLUMN robots_origin text NULL;

CREATE TABLE harvest_fetch_validators (
    url           text        NOT NULL PRIMARY KEY,
    etag          text        NULL,
    last_modified timestamptz NULL,
    body          bytea       NULL,
    content_type  text        NULL,
    updated_at    timestamptz NOT NULL
);
