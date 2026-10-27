-- 001-baseline — the slice-1 schema: registry, engine, capture, link and post layers, delivery log, usage meters.
--
-- The canonical schema: EF maps over this and never generates it. PostgreSQL 16+ with a UTF-8 database and pg_trgm;
-- the module's boot self-test refuses anything else. Tables carry the `harvest_` prefix and live beside a Form 1
-- consumer's own tables; the module's migration set keeps its history in `harvest_migration_history`.
--
-- Rules: ids are UUIDv7 made in code; time columns are timestamptz; enumerations are snake_case text with a CHECK.
-- Container-scoped parents expose UNIQUE (container_id, id) and children reference (container_id, parent_id), so a row
-- never points into another container. `harvest_captures` is range-partitioned by month, so nothing references it by
-- foreign key: `capture_id` columns pair with `captured_at` and are checked in code.
-- Sources: wow-two-ws/ideas/harvest-analysis-2026-10 T5 § 2, T7 § 2–8, T8 § 7 step 2, T9 § 1.5 and § 4.4.

CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- ── Registry ──────────────────────────────────────────────────────────────────────────────────────────────────────

-- A consuming service: owns keys, private containers and subscriptions.
CREATE TABLE harvest_tenants (
    id                           uuid        NOT NULL,
    slug                         text        NOT NULL,
    name                         text        NOT NULL,
    state                        text        NOT NULL DEFAULT 'active',
    platform_credentials_allowed boolean     NOT NULL DEFAULT false,
    created_at                   timestamptz NOT NULL,

    CONSTRAINT pk_harvest_tenants PRIMARY KEY (id),
    CONSTRAINT ux_harvest_tenants_slug UNIQUE (slug),
    CONSTRAINT ck_harvest_tenants_state CHECK (state IN ('active', 'suspended'))
);

-- A tenant's API keys: the SHA-256 of the secret and a display prefix; the secret is never stored.
CREATE TABLE harvest_api_keys (
    id           uuid        NOT NULL,
    tenant_id    uuid        NOT NULL,
    name         text        NOT NULL,
    prefix       text        NOT NULL,
    hash         text        NOT NULL,
    scopes       text[]      NOT NULL DEFAULT '{}',
    created_at   timestamptz NOT NULL,
    expires_at   timestamptz NULL,
    last_used_at timestamptz NULL,
    revoked_at   timestamptz NULL,
    rotated_from uuid        NULL,

    CONSTRAINT pk_harvest_api_keys PRIMARY KEY (id),
    CONSTRAINT ux_harvest_api_keys_hash UNIQUE (hash),
    CONSTRAINT fk_harvest_api_keys_tenant FOREIGN KEY (tenant_id) REFERENCES harvest_tenants (id),
    CONSTRAINT fk_harvest_api_keys_rotated_from FOREIGN KEY (rotated_from) REFERENCES harvest_api_keys (id)
);

CREATE INDEX ix_harvest_api_keys_tenant ON harvest_api_keys (tenant_id, created_at);

-- The visibility boundary: one pool per instance, and private containers per tenant.
CREATE TABLE harvest_containers (
    id                 uuid        NOT NULL,
    kind               text        NOT NULL,
    tenant_id          uuid        NULL,
    external_ref       text        NULL,
    residency          text        NULL,
    embeddings_enabled boolean     NOT NULL DEFAULT false,
    state              text        NOT NULL DEFAULT 'active',
    created_at         timestamptz NOT NULL,

    CONSTRAINT pk_harvest_containers PRIMARY KEY (id),
    CONSTRAINT fk_harvest_containers_tenant FOREIGN KEY (tenant_id) REFERENCES harvest_tenants (id),
    CONSTRAINT ck_harvest_containers_kind CHECK (kind IN ('pool', 'private')),
    CONSTRAINT ck_harvest_containers_owner CHECK ((kind = 'pool') = (tenant_id IS NULL)),
    CONSTRAINT ck_harvest_containers_state CHECK (state IN ('active', 'archived'))
);

CREATE UNIQUE INDEX ux_harvest_containers_pool ON harvest_containers (kind) WHERE kind = 'pool';
CREATE UNIQUE INDEX ux_harvest_containers_external_ref ON harvest_containers (tenant_id, external_ref)
    WHERE external_ref IS NOT NULL;

-- Retention classes: data, so a platform duty is a seeded row; captures copy expiry at insert.
CREATE TABLE harvest_retention_policies (
    key                text     NOT NULL,
    raw_ttl            interval NULL,
    reading_ttl        interval NULL,
    media_ttl          interval NULL,
    metric_ttl         interval NULL,
    refresh_every      interval NULL,
    mandatory          boolean  NOT NULL DEFAULT false,
    delete_request_sla interval NULL,
    description        text     NOT NULL,

    CONSTRAINT pk_harvest_retention_policies PRIMARY KEY (key)
);

INSERT INTO harvest_retention_policies (key, raw_ttl, reading_ttl, media_ttl, metric_ttl, refresh_every, mandatory, delete_request_sla, description) VALUES
    ('standard', interval '365 days', NULL,              interval '90 days', interval '400 days', NULL,              false, NULL,             'web, listings, feeds: raw bodies a year after the last capture; media originals 90 days after gone'),
    ('youtube',  interval '0',        interval '30 days', interval '0',       interval '30 days',  interval '30 days', true,  interval '7 days', 'YouTube Data API: no raw body, refresh or delete within 30 days'),
    ('meta',     interval '30 days',  interval '30 days', interval '30 days', interval '30 days',  interval '30 days', false, interval '7 days', 'Meta API and providers on Instagram and Facebook: refreshed while subscribed'),
    ('x',        NULL,                NULL,               NULL,               NULL,                interval '1 day',   true,  interval '1 day',  'X API: compliance events applied daily'),
    ('telegram', interval '365 days', NULL,               interval '90 days', interval '400 days', NULL,              false, NULL,             'public Telegram, any route: as standard'),
    ('intake',   NULL,                NULL,               interval '90 days', NULL,                NULL,              false, interval '7 days', 'consented submissions: until withdrawal');

-- Deletion-check policies: methods in order and the cadence the engine runs (T5 § 5.5).
CREATE TABLE harvest_check_policies (
    id          uuid   NOT NULL,
    key         text   NOT NULL,
    methods     text[] NOT NULL,
    policy      jsonb  NOT NULL,
    description text   NOT NULL,

    CONSTRAINT pk_harvest_check_policies PRIMARY KEY (id),
    CONSTRAINT ux_harvest_check_policies_key UNIQUE (key)
);

INSERT INTO harvest_check_policies (id, key, methods, policy, description) VALUES
    ('0199d0a0-0000-7000-8000-000000000001', 'web-direct', ARRAY['direct_fetch'],
     '{"cadence":[{"ageUnder":"P2D","every":"PT6H"},{"ageUnder":"P14D","every":"P1D"},{"every":"P7D"}],"byEventTime":{"within":"P14D","every":"P1D","stopAfterEnd":"P7D"},"interest":{"watched":"P1D"},"confirmChecks":1,"suspectResolveWithin":"P3D","enumeration":{"completeOnly":true,"dropGuardPct":30},"relistStableChecks":2,"stopAfter":{"age":"P365D"}}',
     'a conditional GET of the URL; age and event-time bands'),
    ('0199d0a0-0000-7000-8000-000000000002', 'none', ARRAY[]::text[], '{}',
     'no existence check: feeds, intake, owner bots; links abstain from the post rollup');

-- Operator policy per registrable host: the permission switch and the content-signal mode.
CREATE TABLE harvest_host_policies (
    host_key             text        NOT NULL,
    permission           text        NOT NULL DEFAULT 'open',
    content_signals_mode text        NOT NULL DEFAULT 'record',
    content_signals      jsonb       NULL,
    note                 text        NULL,
    updated_at           timestamptz NOT NULL,

    CONSTRAINT pk_harvest_host_policies PRIMARY KEY (host_key),
    CONSTRAINT ck_harvest_host_policies_permission CHECK (permission IN ('open', 'restricted', 'granted', 'denied')),
    CONSTRAINT ck_harvest_host_policies_signals CHECK (content_signals_mode IN ('record', 'enforce'))
);

-- Where posts come from, with the posture switches (permissive defaults) and the capability policy.
CREATE TABLE harvest_sources (
    id                uuid        NOT NULL,
    container_id      uuid        NOT NULL,
    family            text        NOT NULL,
    handle            text        NOT NULL,
    external_ref      text        NULL,
    coverage          text        NOT NULL,
    completeness      text        NOT NULL,
    posture           text        NOT NULL,
    permission        text        NOT NULL DEFAULT 'open',
    ai_allowed        boolean     NOT NULL DEFAULT true,
    pool_shareable    boolean     NOT NULL DEFAULT true,
    robots_policy     text        NOT NULL DEFAULT 'obey',
    consent_basis     text        NOT NULL DEFAULT 'none',
    authority         smallint    NOT NULL DEFAULT 50,
    retention_class   text        NOT NULL DEFAULT 'standard',
    check_policy_id   uuid        NULL,
    capability_policy jsonb       NOT NULL DEFAULT '{}',
    state             text        NOT NULL DEFAULT 'active',
    state_since       timestamptz NOT NULL,
    created_by        text        NULL,
    created_at        timestamptz NOT NULL,

    CONSTRAINT pk_harvest_sources PRIMARY KEY (id),
    CONSTRAINT ux_harvest_sources_container_id UNIQUE (container_id, id),
    CONSTRAINT ux_harvest_sources_handle UNIQUE (container_id, family, handle),
    CONSTRAINT fk_harvest_sources_container FOREIGN KEY (container_id) REFERENCES harvest_containers (id),
    CONSTRAINT fk_harvest_sources_retention FOREIGN KEY (retention_class) REFERENCES harvest_retention_policies (key),
    CONSTRAINT fk_harvest_sources_check_policy FOREIGN KEY (check_policy_id) REFERENCES harvest_check_policies (id),
    CONSTRAINT ck_harvest_sources_family CHECK (family IN ('page', 'structured', 'site', 'render', 'api', 'feed', 'link',
        'listing', 'telegram', 'whatsapp', 'instagram', 'facebook', 'youtube', 'threads', 'x', 'intake', 'manual')),
    CONSTRAINT ck_harvest_sources_coverage CHECK (coverage IN ('whole', 'filtered', 'single')),
    CONSTRAINT ck_harvest_sources_completeness CHECK (completeness IN ('complete', 'window', 'none')),
    CONSTRAINT ck_harvest_sources_posture CHECK (posture IN ('official', 'licensed_provider', 'own_fetch', 'consented_intake')),
    CONSTRAINT ck_harvest_sources_permission CHECK (permission IN ('open', 'restricted', 'granted', 'denied')),
    CONSTRAINT ck_harvest_sources_robots CHECK (robots_policy IN ('obey', 'ignore')),
    CONSTRAINT ck_harvest_sources_consent CHECK (consent_basis IN ('owner', 'submitter', 'none')),
    CONSTRAINT ck_harvest_sources_authority CHECK (authority BETWEEN 0 AND 100),
    CONSTRAINT ck_harvest_sources_state CHECK (state IN ('active', 'paused', 'unavailable', 'preview_unavailable',
        'quota_exhausted', 'retired'))
);

-- Host and path patterns a whole-coverage source owns, for link routing.
CREATE TABLE harvest_source_scopes (
    id           uuid        NOT NULL,
    container_id uuid        NOT NULL,
    source_id    uuid        NOT NULL,
    host         text        NOT NULL,
    path_prefix  text        NOT NULL DEFAULT '/',
    created_at   timestamptz NOT NULL,

    CONSTRAINT pk_harvest_source_scopes PRIMARY KEY (id),
    CONSTRAINT ux_harvest_source_scopes UNIQUE (source_id, host, path_prefix),
    CONSTRAINT fk_harvest_source_scopes_source FOREIGN KEY (container_id, source_id)
        REFERENCES harvest_sources (container_id, id) ON DELETE CASCADE
);

CREATE INDEX ix_harvest_source_scopes_route ON harvest_source_scopes (host, path_prefix text_pattern_ops);

-- ── Engine ────────────────────────────────────────────────────────────────────────────────────────────────────────

-- A standing harvest of a source; identity (container, source, scope_key); step overrides by step key.
CREATE TABLE harvest_pipelines (
    id             uuid        NOT NULL,
    container_id   uuid        NOT NULL,
    source_id      uuid        NOT NULL,
    scope_key      text        NOT NULL,
    scope          jsonb       NOT NULL DEFAULT '{}',
    name           text        NULL,
    state          text        NOT NULL,
    pause_reason   text        NULL,
    state_since    timestamptz NOT NULL,
    pinned         boolean     NOT NULL DEFAULT false,
    ai_mode        text        NOT NULL DEFAULT 'off',
    limits         jsonb       NOT NULL DEFAULT '{}',
    step_overrides jsonb       NOT NULL DEFAULT '{}',
    created_by     text        NULL,
    created_at     timestamptz NOT NULL,

    CONSTRAINT pk_harvest_pipelines PRIMARY KEY (id),
    CONSTRAINT ux_harvest_pipelines_container_id UNIQUE (container_id, id),
    CONSTRAINT ux_harvest_pipelines_identity UNIQUE (container_id, source_id, scope_key),
    CONSTRAINT fk_harvest_pipelines_source FOREIGN KEY (container_id, source_id) REFERENCES harvest_sources (container_id, id),
    CONSTRAINT ck_harvest_pipelines_state CHECK (state IN ('proposed', 'pending_approval', 'active', 'paused', 'archived')),
    CONSTRAINT ck_harvest_pipelines_pause CHECK (
        (state = 'paused') = (pause_reason IS NOT NULL)
        AND (pause_reason IS NULL OR pause_reason IN ('operator', 'budget_hard', 'permission', 'account', 'idle'))),
    CONSTRAINT ck_harvest_pipelines_ai CHECK (ai_mode IN ('off', 'gaps', 'full'))
);

-- One cadence row per pipeline and work kind; next_due_at is stored, so downtime coalesces instead of losing a tick.
CREATE TABLE harvest_schedules (
    id             uuid        NOT NULL,
    container_id   uuid        NOT NULL,
    pipeline_id    uuid        NOT NULL,
    kind           text        NOT NULL,
    mode           text        NOT NULL,
    every_interval interval    NULL,
    max_interval   interval    NULL,
    cron           text        NULL,
    time_zone      text        NOT NULL DEFAULT 'Asia/Tashkent',
    window_start   time        NULL,
    window_end     time        NULL,
    enabled        boolean     NOT NULL DEFAULT true,
    next_due_at    timestamptz NULL,
    last_queued_at timestamptz NULL,
    created_at     timestamptz NOT NULL,

    CONSTRAINT pk_harvest_schedules PRIMARY KEY (id),
    CONSTRAINT ux_harvest_schedules_kind UNIQUE (pipeline_id, kind),
    CONSTRAINT fk_harvest_schedules_pipeline FOREIGN KEY (container_id, pipeline_id)
        REFERENCES harvest_pipelines (container_id, id) ON DELETE CASCADE,
    CONSTRAINT ck_harvest_schedules_kind CHECK (kind IN ('capture', 'discover', 'refresh', 'check', 'metrics')),
    CONSTRAINT ck_harvest_schedules_mode CHECK (
        (mode = 'manual')
        OR (mode = 'interval' AND every_interval IS NOT NULL)
        OR (mode = 'cron' AND cron IS NOT NULL)
        OR (mode = 'adaptive' AND every_interval IS NOT NULL AND max_interval IS NOT NULL AND max_interval >= every_interval)),
    CONSTRAINT ck_harvest_schedules_window CHECK ((window_start IS NULL) = (window_end IS NULL))
);

CREATE INDEX ix_harvest_schedules_due ON harvest_schedules (next_due_at) WHERE enabled;

-- One execution of a schedule row or a trigger; settings frozen at queue time; a lease replaces boot-time recovery.
CREATE TABLE harvest_runs (
    id                     uuid          NOT NULL,
    container_id           uuid          NOT NULL,
    pipeline_id            uuid          NOT NULL,
    schedule_id            uuid          NULL,
    kind                   text          NOT NULL,
    trigger                text          NOT NULL,
    scheduled_for          timestamptz   NOT NULL,
    frozen                 jsonb         NOT NULL DEFAULT '{}',
    status                 text          NOT NULL DEFAULT 'queued',
    stage                  text          NULL,
    steps                  jsonb         NOT NULL DEFAULT '[]',
    pages                  integer       NOT NULL DEFAULT 0,
    discovered             integer       NOT NULL DEFAULT 0,
    captured               integer       NOT NULL DEFAULT 0,
    unchanged              integer       NOT NULL DEFAULT 0,
    failed                 integer       NOT NULL DEFAULT 0,
    gone                   integer       NOT NULL DEFAULT 0,
    skipped                integer       NOT NULL DEFAULT 0,
    requests               integer       NOT NULL DEFAULT 0,
    bytes                  bigint        NOT NULL DEFAULT 0,
    cost_usd_est           numeric(12,4) NOT NULL DEFAULT 0,
    cost_usd_billed        numeric(12,4) NULL,
    enumeration_complete   boolean       NOT NULL DEFAULT false,
    lease_owner            text          NULL,
    lease_until            timestamptz   NULL,
    heartbeat_at           timestamptz   NULL,
    revision               integer       NOT NULL DEFAULT 1,
    cancellation_requested boolean       NOT NULL DEFAULT false,
    errors                 jsonb         NOT NULL DEFAULT '[]',
    completion_reason      text          NULL,
    fetch_outcome          text          NULL,
    message                text          NULL,
    queued_at              timestamptz   NOT NULL,
    started_at             timestamptz   NULL,
    finished_at            timestamptz   NULL,

    CONSTRAINT pk_harvest_runs PRIMARY KEY (id),
    CONSTRAINT ux_harvest_runs_container_id UNIQUE (container_id, id),
    CONSTRAINT ux_harvest_runs_slot UNIQUE (pipeline_id, kind, scheduled_for),
    CONSTRAINT fk_harvest_runs_pipeline FOREIGN KEY (container_id, pipeline_id) REFERENCES harvest_pipelines (container_id, id),
    CONSTRAINT fk_harvest_runs_schedule FOREIGN KEY (schedule_id) REFERENCES harvest_schedules (id) ON DELETE SET NULL,
    CONSTRAINT ck_harvest_runs_kind CHECK (kind IN ('capture', 'discover', 'refresh', 'sweep', 'check', 'metrics', 'link', 'intake')),
    CONSTRAINT ck_harvest_runs_trigger CHECK (trigger IN ('manual', 'schedule', 'continuation', 'join', 'intake', 'link')),
    CONSTRAINT ck_harvest_runs_status CHECK (status IN ('queued', 'running', 'completed', 'partial', 'failed', 'cancelled',
        'interrupted', 'paused'))
);

-- One active run per pipeline and kind; a second request is refused while one is queued, running or paused.
CREATE UNIQUE INDEX ux_harvest_runs_active ON harvest_runs (pipeline_id, kind) WHERE status IN ('queued', 'running', 'paused');
CREATE INDEX ix_harvest_runs_lease ON harvest_runs (lease_until) WHERE status = 'running';
CREATE INDEX ix_harvest_runs_pipeline ON harvest_runs (pipeline_id, queued_at DESC);

-- The claimable unit and the frontier: claimed by (host_key, lane) with FOR UPDATE SKIP LOCKED, results fenced on the
-- lease. `rung` is set at claim; `route` derives from it; `plan_ref` is the frozen requirements.
CREATE TABLE harvest_work_item (
    id               uuid        NOT NULL,
    container_id     uuid        NOT NULL,
    run_id           uuid        NOT NULL,
    pipeline_id      uuid        NOT NULL,
    host_key         text        NOT NULL,
    bucket_key       text        NULL,
    step             text        NOT NULL,
    lane             text        NOT NULL DEFAULT 'default',
    plan_ref         jsonb       NOT NULL DEFAULT '{}',
    rung             text        NULL,
    route            text        NULL,
    egress_lease_id  uuid        NULL,
    session_lease_id uuid        NULL,
    target           text        NOT NULL,
    payload          jsonb       NULL,
    state            text        NOT NULL DEFAULT 'queued',
    priority         smallint    NOT NULL DEFAULT 3,
    due_at           timestamptz NOT NULL,
    not_before       timestamptz NOT NULL,
    attempts         integer     NOT NULL DEFAULT 0,
    lease_owner      text        NULL,
    lease_until      timestamptz NULL,
    outcome          text        NULL,
    last_error       text        NULL,
    created_at       timestamptz NOT NULL,
    finished_at      timestamptz NULL,

    CONSTRAINT pk_harvest_work_item PRIMARY KEY (id),
    CONSTRAINT ux_harvest_work_item_target UNIQUE (run_id, step, target),
    CONSTRAINT fk_harvest_work_item_run FOREIGN KEY (container_id, run_id) REFERENCES harvest_runs (container_id, id) ON DELETE CASCADE,
    CONSTRAINT fk_harvest_work_item_pipeline FOREIGN KEY (container_id, pipeline_id) REFERENCES harvest_pipelines (container_id, id),
    CONSTRAINT ck_harvest_work_item_state CHECK (state IN ('queued', 'running', 'done', 'failed', 'gone', 'blocked')),
    CONSTRAINT ck_harvest_work_item_route CHECK (route IS NULL OR route IN ('static', 'render', 'api', 'provider', 'account')),
    CONSTRAINT ck_harvest_work_item_rung CHECK (rung IS NULL OR rung IN ('R0', 'R1', 'R2', 'R3', 'R4', 'R5', 'R6', 'R7', 'R8')),
    CONSTRAINT ck_harvest_work_item_priority CHECK (priority BETWEEN 0 AND 4)
);

CREATE INDEX ix_harvest_work_item_claim ON harvest_work_item (host_key, lane, priority DESC, due_at) WHERE state = 'queued';
CREATE INDEX ix_harvest_work_item_lease ON harvest_work_item (lease_until) WHERE state = 'running';
CREATE INDEX ix_harvest_work_item_run ON harvest_work_item (run_id, state);

-- The global gate per host, across tenants, containers and pipelines; politeness lives here, not in a run.
CREATE TABLE harvest_host_state (
    host_key             text        NOT NULL,
    max_concurrency      smallint    NOT NULL DEFAULT 1,
    min_delay_ms         integer     NOT NULL DEFAULT 2000,
    adaptive_delay_ms    integer     NOT NULL DEFAULT 2000,
    crawl_delay_s        integer     NULL,
    retry_after_until    timestamptz NULL,
    paused_until         timestamptz NULL,
    pause_reason         text        NULL,
    consecutive_failures integer     NOT NULL DEFAULT 0,
    next_allowed_at      timestamptz NOT NULL DEFAULT now(),
    in_flight            integer     NOT NULL DEFAULT 0,
    window_start         time        NULL,
    window_end           time        NULL,
    window_time_zone     text        NULL,
    egress               text        NULL,
    robots_status        smallint    NULL,
    robots_body          text        NULL,
    robots_cached_at     timestamptz NULL,
    permission           text        NOT NULL DEFAULT 'open',
    updated_at           timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_harvest_host_state PRIMARY KEY (host_key),
    CONSTRAINT ck_harvest_host_state_concurrency CHECK (max_concurrency > 0 AND in_flight >= 0),
    CONSTRAINT ck_harvest_host_state_permission CHECK (permission IN ('open', 'restricted', 'granted', 'denied')),
    CONSTRAINT ck_harvest_host_state_window CHECK ((window_start IS NULL) = (window_end IS NULL))
);

CREATE INDEX ix_harvest_host_state_gate ON harvest_host_state (next_allowed_at);

-- The learned rung per (host, lane): one step can need different egress from the rest of its host.
CREATE TABLE harvest_host_lane (
    host_key             text          NOT NULL,
    lane                 text          NOT NULL,
    rung                 text          NOT NULL DEFAULT 'R0',
    rung_since           timestamptz   NOT NULL DEFAULT now(),
    consecutive_failures integer       NOT NULL DEFAULT 0,
    last_challenge_kind  text          NULL,
    paused_until         timestamptz   NULL,
    probe_due_at         timestamptz   NULL,
    success_ewma         double precision NOT NULL DEFAULT 1,
    cost_per_success_usd numeric(12,6) NULL,
    max_concurrency      smallint      NOT NULL DEFAULT 1,
    in_flight            integer       NOT NULL DEFAULT 0,
    updated_at           timestamptz   NOT NULL DEFAULT now(),

    CONSTRAINT pk_harvest_host_lane PRIMARY KEY (host_key, lane),
    CONSTRAINT fk_harvest_host_lane_host FOREIGN KEY (host_key) REFERENCES harvest_host_state (host_key) ON DELETE CASCADE,
    CONSTRAINT ck_harvest_host_lane_rung CHECK (rung IN ('R0', 'R1', 'R2', 'R3', 'R4', 'R5', 'R6', 'R7', 'R8')),
    CONSTRAINT ck_harvest_host_lane_concurrency CHECK (max_concurrency > 0 AND in_flight >= 0)
);

-- Usage meters: what each paid or counted unit spent, attributed to run, item, pipeline, tenant and subscription.
CREATE TABLE harvest_usage (
    id               uuid          NOT NULL,
    at               timestamptz   NOT NULL,
    container_id     uuid          NULL,
    run_id           uuid          NULL,
    work_item_id     uuid          NULL,
    pipeline_id      uuid          NULL,
    tenant_id        uuid          NULL,
    subscription_id  uuid          NULL,
    provider_account text          NULL,
    host_key         text          NULL,
    lane             text          NULL,
    rung             text          NULL,
    meter            text          NOT NULL,
    units            numeric(18,4) NOT NULL,
    usd_estimated    numeric(12,6) NULL,
    usd_billed       numeric(12,6) NULL,
    source           text          NOT NULL,

    CONSTRAINT pk_harvest_usage PRIMARY KEY (id),
    CONSTRAINT ck_harvest_usage_meter CHECK (meter IN ('requests', 'bytes', 'egress_bytes', 'egress_ip_months', 'solves',
        'unlocker_requests', 'render_seconds', 'provider_results', 'api_units', 'llm_tokens')),
    CONSTRAINT ck_harvest_usage_source CHECK (source IN ('provider_reported', 'estimate'))
);

CREATE INDEX ix_harvest_usage_pipeline ON harvest_usage (pipeline_id, at);
CREATE INDEX ix_harvest_usage_tenant ON harvest_usage (tenant_id, at);
CREATE INDEX ix_harvest_usage_run ON harvest_usage (run_id);

-- ── Captures ──────────────────────────────────────────────────────────────────────────────────────────────────────

-- One pass over a sitemap, feed or channel list: complete or window, count, and the drop guard.
CREATE TABLE harvest_enumerations (
    id             uuid        NOT NULL,
    container_id   uuid        NOT NULL,
    source_id      uuid        NOT NULL,
    run_id         uuid        NULL,
    kind           text        NOT NULL,
    started_at     timestamptz NOT NULL,
    finished_at    timestamptz NULL,
    complete       boolean     NOT NULL DEFAULT false,
    item_count     integer     NOT NULL DEFAULT 0,
    previous_count integer     NULL,
    guard_tripped  boolean     NOT NULL DEFAULT false,

    CONSTRAINT pk_harvest_enumerations PRIMARY KEY (id),
    CONSTRAINT fk_harvest_enumerations_source FOREIGN KEY (container_id, source_id) REFERENCES harvest_sources (container_id, id),
    CONSTRAINT fk_harvest_enumerations_run FOREIGN KEY (container_id, run_id) REFERENCES harvest_runs (container_id, id)
);

CREATE INDEX ix_harvest_enumerations_source ON harvest_enumerations (source_id, started_at DESC);

-- Immutable dated snapshots of one link, written only when the content hash changes; bodies live in file storage
-- (`body_ref` is an opaque reference). Monthly range partitions on captured_at, plus a default partition for strays.
CREATE TABLE harvest_captures (
    id              uuid        NOT NULL,
    container_id    uuid        NOT NULL,
    source_id       uuid        NOT NULL,
    link_id         uuid        NULL,
    run_id          uuid        NULL,
    captured_at     timestamptz NOT NULL,
    request_url     text        NOT NULL,
    final_url       text        NOT NULL,
    outcome         text        NOT NULL DEFAULT 'ok',
    http_status     smallint    NULL,
    route           text        NULL,
    rung            text        NULL,
    posture         text        NOT NULL,
    provider        text        NULL,
    logged_out      boolean     NULL,
    terms_version   text        NULL,
    raw_hash        bytea       NOT NULL,
    content_hash    bytea       NOT NULL,
    body_ref        text        NULL,
    body_bytes      integer     NOT NULL DEFAULT 0,
    content_type    text        NULL,
    retention_class text        NOT NULL,
    expires_at      timestamptz NULL,
    scrubbed_at     timestamptz NULL,
    scrub_reason    text        NULL,
    warnings        jsonb       NOT NULL DEFAULT '[]',

    CONSTRAINT pk_harvest_captures PRIMARY KEY (captured_at, id),
    CONSTRAINT ck_harvest_captures_outcome CHECK (outcome = 'ok'),
    CONSTRAINT ck_harvest_captures_hashes CHECK (octet_length(raw_hash) = 32 AND octet_length(content_hash) = 32)
) PARTITION BY RANGE (captured_at);

CREATE TABLE harvest_captures_default PARTITION OF harvest_captures DEFAULT;

CREATE INDEX ix_harvest_captures_link ON harvest_captures (link_id, captured_at DESC);
CREATE INDEX ix_harvest_captures_expiry ON harvest_captures (expires_at) WHERE scrubbed_at IS NULL;

-- Creates the monthly partitions from the current UTC month through `months_ahead` months on; idempotent, so the
-- maintenance job reruns it. A month whose rows already sit in the default partition must be moved out first.
CREATE FUNCTION harvest_ensure_capture_partitions(months_ahead integer) RETURNS integer
LANGUAGE plpgsql AS $$
DECLARE
    first_month date := date_trunc('month', now() AT TIME ZONE 'UTC')::date;
    month_start date;
    partition_name text;
    created     integer := 0;
BEGIN
    FOR offset_months IN 0..months_ahead LOOP
        month_start := (first_month + make_interval(months => offset_months))::date;
        partition_name := format('harvest_captures_y%sm%s', to_char(month_start, 'YYYY'), to_char(month_start, 'MM'));
        IF to_regclass(partition_name) IS NULL THEN
            EXECUTE format('CREATE TABLE %I PARTITION OF harvest_captures FOR VALUES FROM (%L) TO (%L)',
                partition_name,
                month_start::timestamp AT TIME ZONE 'UTC',
                (month_start + interval '1 month')::timestamp AT TIME ZONE 'UTC');
            created := created + 1;
        END IF;
    END LOOP;
    RETURN created;
END
$$;

SELECT harvest_ensure_capture_partitions(3);

-- ── Posts and links ───────────────────────────────────────────────────────────────────────────────────────────────

-- The canonical post: derived from its links' readings by field precedence, never patched in place.
CREATE TABLE harvest_posts (
    id              uuid        NOT NULL,
    container_id    uuid        NOT NULL,
    kind            text        NOT NULL,
    state           text        NOT NULL DEFAULT 'live',
    state_since     timestamptz NOT NULL,
    verifiable      boolean     NOT NULL DEFAULT true,
    relisted_at     timestamptz NULL,
    relist_count    integer     NOT NULL DEFAULT 0,
    alias_of        uuid        NULL,
    merged_into     uuid        NULL,
    purged_at       timestamptz NULL,
    primary_locale  text        NULL,
    published_at    timestamptz NULL,
    base            jsonb       NOT NULL DEFAULT '{}',
    field_prov      jsonb       NOT NULL DEFAULT '{}',
    dedup_state     text        NOT NULL DEFAULT 'pending',
    revision        integer     NOT NULL DEFAULT 1,
    first_seen_at   timestamptz NOT NULL,
    last_changed_at timestamptz NOT NULL,

    CONSTRAINT pk_harvest_posts PRIMARY KEY (id),
    CONSTRAINT ux_harvest_posts_container_id UNIQUE (container_id, id),
    CONSTRAINT fk_harvest_posts_container FOREIGN KEY (container_id) REFERENCES harvest_containers (id),
    -- the one cross-container edge: a private post showing a pool post
    CONSTRAINT fk_harvest_posts_alias_of FOREIGN KEY (alias_of) REFERENCES harvest_posts (id),
    CONSTRAINT fk_harvest_posts_merged_into FOREIGN KEY (container_id, merged_into) REFERENCES harvest_posts (container_id, id),
    CONSTRAINT ck_harvest_posts_state CHECK (state IN ('live', 'suspect', 'gone')),
    CONSTRAINT ck_harvest_posts_dedup CHECK (dedup_state IN ('pending', 'settled'))
);

CREATE INDEX ix_harvest_posts_kind ON harvest_posts (container_id, kind, first_seen_at DESC);
CREATE INDEX ix_harvest_posts_state ON harvest_posts (container_id, state, state_since);
CREATE INDEX ix_harvest_posts_alias_of ON harvest_posts (alias_of) WHERE alias_of IS NOT NULL;
CREATE INDEX ix_harvest_posts_merged_into ON harvest_posts (merged_into) WHERE merged_into IS NOT NULL;

-- Text per locale, with the normalized, transliterated match text the trigram index reads.
CREATE TABLE harvest_post_texts (
    post_id        uuid        NOT NULL,
    container_id   uuid        NOT NULL,
    locale         text        NOT NULL,
    title          text        NULL,
    body           text        NULL,
    match_text     text        NOT NULL DEFAULT '',
    text_norm_hash bytea       NULL,
    num_sig        bytea       NULL,
    link_id        uuid        NULL,
    updated_at     timestamptz NOT NULL,

    CONSTRAINT pk_harvest_post_texts PRIMARY KEY (post_id, locale),
    CONSTRAINT fk_harvest_post_texts_post FOREIGN KEY (container_id, post_id) REFERENCES harvest_posts (container_id, id) ON DELETE CASCADE,
    CONSTRAINT ck_harvest_post_texts_match CHECK (char_length(match_text) <= 1000),
    CONSTRAINT ck_harvest_post_texts_hashes CHECK (
        (text_norm_hash IS NULL OR octet_length(text_norm_hash) = 16) AND (num_sig IS NULL OR octet_length(num_sig) = 16))
);

CREATE INDEX ix_harvest_post_texts_match ON harvest_post_texts USING gin (match_text gin_trgm_ops);
CREATE INDEX ix_harvest_post_texts_norm_hash ON harvest_post_texts (container_id, text_norm_hash) WHERE text_norm_hash IS NOT NULL;

-- One post as seen at one source URL or id, with its own state; merges re-point it (move_link) and undo.
CREATE TABLE harvest_post_links (
    id                  uuid        NOT NULL,
    container_id        uuid        NOT NULL,
    source_id           uuid        NOT NULL,
    post_id             uuid        NOT NULL,
    external_key        text        NOT NULL,
    url                 text        NOT NULL,
    url_key             bytea       NOT NULL,
    origin_key          text        NULL,
    role                text        NOT NULL DEFAULT 'primary',
    parent_link_id      uuid        NULL,
    locale              text        NULL,
    state               text        NOT NULL DEFAULT 'live',
    state_since         timestamptz NOT NULL,
    retired_at          timestamptz NULL,
    retired_reason      text        NULL,
    check_capability    text        NOT NULL DEFAULT 'full',
    consent_id          uuid        NULL,
    first_seen_at       timestamptz NOT NULL,
    last_changed_at     timestamptz NOT NULL,
    gone_at             timestamptz NULL,
    relist_count        integer     NOT NULL DEFAULT 0,
    latest_capture_id   uuid        NULL,
    latest_captured_at  timestamptz NULL,
    latest_content_hash bytea       NULL,

    CONSTRAINT pk_harvest_post_links PRIMARY KEY (id),
    CONSTRAINT ux_harvest_post_links_container_id UNIQUE (container_id, id),
    CONSTRAINT ux_harvest_post_links_identity UNIQUE (source_id, external_key),
    CONSTRAINT fk_harvest_post_links_source FOREIGN KEY (container_id, source_id) REFERENCES harvest_sources (container_id, id),
    CONSTRAINT fk_harvest_post_links_post FOREIGN KEY (container_id, post_id) REFERENCES harvest_posts (container_id, id),
    CONSTRAINT fk_harvest_post_links_parent FOREIGN KEY (container_id, parent_link_id) REFERENCES harvest_post_links (container_id, id),
    CONSTRAINT ck_harvest_post_links_url_key CHECK (octet_length(url_key) = 16),
    CONSTRAINT ck_harvest_post_links_role CHECK (role IN ('primary', 'alternate', 'copy', 'part', 'reply')),
    CONSTRAINT ck_harvest_post_links_state CHECK (state IN ('live', 'suspect', 'gone', 'relisted')),
    CONSTRAINT ck_harvest_post_links_retired CHECK (
        (retired_at IS NULL) = (retired_reason IS NULL)
        AND (retired_reason IS NULL OR retired_reason IN ('moved', 'merged_duplicate', 'covered_by_pool', 'source_removed'))),
    CONSTRAINT ck_harvest_post_links_check CHECK (check_capability IN ('full', 'weak', 'none'))
);

CREATE INDEX ix_harvest_post_links_post ON harvest_post_links (container_id, post_id);
CREATE INDEX ix_harvest_post_links_url_key ON harvest_post_links (container_id, url_key);
CREATE INDEX ix_harvest_post_links_origin_key ON harvest_post_links (container_id, origin_key) WHERE origin_key IS NOT NULL;

-- The hot row per link: every poll and check rewrites it, so the wide link row stays still.
CREATE TABLE harvest_link_schedule (
    link_id             uuid        NOT NULL,
    container_id        uuid        NOT NULL,
    next_check_at       timestamptz NULL,
    last_checked_at     timestamptz NULL,
    last_seen_at        timestamptz NULL,
    last_enumerated_at  timestamptz NULL,
    check_level         smallint    NOT NULL DEFAULT 0,
    check_count         integer     NOT NULL DEFAULT 0,
    fail_count          integer     NOT NULL DEFAULT 0,
    consecutive_missing integer     NOT NULL DEFAULT 0,
    last_outcome        text        NULL,
    due                 boolean     NOT NULL DEFAULT true,

    CONSTRAINT pk_harvest_link_schedule PRIMARY KEY (link_id),
    CONSTRAINT fk_harvest_link_schedule_link FOREIGN KEY (container_id, link_id) REFERENCES harvest_post_links (container_id, id) ON DELETE CASCADE
) WITH (fillfactor = 70, autovacuum_vacuum_scale_factor = 0.02, autovacuum_analyze_scale_factor = 0.02);

CREATE INDEX ix_harvest_link_schedule_due ON harvest_link_schedule (next_check_at) WHERE due;

-- The latest extraction of one link: its own text and the envelope fields, before resolution.
CREATE TABLE harvest_link_readings (
    link_id           uuid        NOT NULL,
    container_id      uuid        NOT NULL,
    capture_id        uuid        NOT NULL,
    captured_at       timestamptz NOT NULL,
    extractor_version text        NOT NULL,
    locale            text        NULL,
    title             text        NULL,
    body              text        NULL,
    fields            jsonb       NOT NULL DEFAULT '{}',
    tier_map          jsonb       NOT NULL DEFAULT '{}',
    ai                jsonb       NULL,
    contacts          jsonb       NULL,
    updated_at        timestamptz NOT NULL,

    CONSTRAINT pk_harvest_link_readings PRIMARY KEY (link_id),
    CONSTRAINT fk_harvest_link_readings_link FOREIGN KEY (container_id, link_id) REFERENCES harvest_post_links (container_id, id) ON DELETE CASCADE
);

-- Link state history, append-only, with the evidence of each transition.
CREATE TABLE harvest_link_events (
    id           uuid        NOT NULL,
    container_id uuid        NOT NULL,
    link_id      uuid        NOT NULL,
    at           timestamptz NOT NULL,
    from_state   text        NULL,
    to_state     text        NOT NULL,
    reason       text        NOT NULL,
    evidence     jsonb       NOT NULL DEFAULT '{}',
    run_id       uuid        NULL,
    actor        text        NULL,

    CONSTRAINT pk_harvest_link_events PRIMARY KEY (id),
    CONSTRAINT fk_harvest_link_events_link FOREIGN KEY (container_id, link_id) REFERENCES harvest_post_links (container_id, id) ON DELETE CASCADE,
    CONSTRAINT ck_harvest_link_events_to CHECK (to_state IN ('live', 'suspect', 'gone', 'relisted')),
    CONSTRAINT ck_harvest_link_events_reason CHECK (reason IN ('direct_410', 'direct_404', 'named_redirect', 'soft_404',
        'node_vanished', 'absent_from_enumeration', 'embed_not_found', 'mtproto_empty', 'delete_update',
        'provider_error_item', 'compliance_delete', 'api_missing', 'seen_again', 'manual'))
);

CREATE INDEX ix_harvest_link_events_link ON harvest_link_events (link_id, at);
CREATE INDEX ix_harvest_link_events_container ON harvest_link_events (container_id, at);

-- Every routine check with its evidence; a maintenance job keeps 30 days.
CREATE TABLE harvest_link_check_log (
    id           uuid        NOT NULL,
    container_id uuid        NOT NULL,
    link_id      uuid        NOT NULL,
    checked_at   timestamptz NOT NULL,
    method       text        NOT NULL,
    outcome      text        NOT NULL,
    http_status  smallint    NULL,
    rung         text        NULL,
    run_id       uuid        NULL,
    work_item_id uuid        NULL,
    evidence     jsonb       NOT NULL DEFAULT '{}',

    CONSTRAINT pk_harvest_link_check_log PRIMARY KEY (id),
    CONSTRAINT fk_harvest_link_check_log_link FOREIGN KEY (container_id, link_id) REFERENCES harvest_post_links (container_id, id) ON DELETE CASCADE
);

CREATE INDEX ix_harvest_link_check_log_link ON harvest_link_check_log (link_id, checked_at DESC);
CREATE INDEX ix_harvest_link_check_log_age ON harvest_link_check_log (checked_at);

-- ── Delivery ──────────────────────────────────────────────────────────────────────────────────────────────────────

-- One tenant's interest — a channel, tags or keywords — with its own delivery; a channel subscription follows a
-- pipeline, a pool subscription keeps none alive.
CREATE TABLE harvest_subscriptions (
    id             uuid        NOT NULL,
    tenant_id      uuid        NOT NULL,
    container_id   uuid        NOT NULL,
    pipeline_id    uuid        NULL,
    kind           text        NOT NULL,
    mode           text        NOT NULL,
    rules          jsonb       NOT NULL DEFAULT '[]',
    context        jsonb       NOT NULL DEFAULT '{}',
    wants_ai       boolean     NOT NULL DEFAULT false,
    schema         text        NULL,
    max_staleness  interval    NULL,
    backfill       text        NOT NULL DEFAULT 'live',
    backfill_since timestamptz NULL,
    webhook_url    text        NULL,
    webhook_secret text        NULL,
    endpoint_state text        NOT NULL DEFAULT 'healthy',
    state          text        NOT NULL DEFAULT 'active',
    last_read_at   timestamptz NULL,
    created_at     timestamptz NOT NULL,

    CONSTRAINT pk_harvest_subscriptions PRIMARY KEY (id),
    CONSTRAINT fk_harvest_subscriptions_tenant FOREIGN KEY (tenant_id) REFERENCES harvest_tenants (id),
    CONSTRAINT fk_harvest_subscriptions_container FOREIGN KEY (container_id) REFERENCES harvest_containers (id),
    CONSTRAINT fk_harvest_subscriptions_pipeline FOREIGN KEY (container_id, pipeline_id) REFERENCES harvest_pipelines (container_id, id),
    CONSTRAINT ck_harvest_subscriptions_kind CHECK (kind IN ('channel', 'tag', 'keyword')),
    CONSTRAINT ck_harvest_subscriptions_channel CHECK ((kind = 'channel') = (pipeline_id IS NOT NULL)),
    CONSTRAINT ck_harvest_subscriptions_mode CHECK (mode IN ('webhook', 'pull', 'both')),
    CONSTRAINT ck_harvest_subscriptions_webhook CHECK (mode = 'pull' OR webhook_url IS NOT NULL),
    CONSTRAINT ck_harvest_subscriptions_backfill CHECK ((backfill = 'since') = (backfill_since IS NOT NULL)
        AND backfill IN ('live', 'none', 'since')),
    CONSTRAINT ck_harvest_subscriptions_endpoint CHECK (endpoint_state IN ('healthy', 'degraded', 'suspended')),
    CONSTRAINT ck_harvest_subscriptions_state CHECK (state IN ('active', 'suspended', 'archived'))
);

CREATE INDEX ix_harvest_subscriptions_tenant ON harvest_subscriptions (tenant_id);
CREATE INDEX ix_harvest_subscriptions_pipeline ON harvest_subscriptions (pipeline_id) WHERE pipeline_id IS NOT NULL;

-- One durable event log per subscription: references, not payloads. Webhook push and the pull cursor read the same
-- rows. The cursor is (tx, id) read below the snapshot horizon, so a poller never skips a row committed late.
CREATE TABLE harvest_subscription_event (
    id              bigint      GENERATED ALWAYS AS IDENTITY,
    tx              xid8        NOT NULL DEFAULT pg_current_xact_id(),
    subscription_id uuid        NOT NULL,
    event_id        uuid        NOT NULL,
    item_id         text        NOT NULL,
    type            text        NOT NULL,
    post_id         uuid        NULL,
    post_key        text        NULL,
    revision        integer     NULL,
    link_id         uuid        NULL,
    capture_id      uuid        NULL,
    run_id          uuid        NULL,
    reason          text        NULL,
    matched         jsonb       NULL,
    occurred_at     timestamptz NOT NULL,
    delivery_state  text        NOT NULL DEFAULT 'pending',
    attempts        integer     NOT NULL DEFAULT 0,
    next_attempt_at timestamptz NULL,
    last_status     smallint    NULL,
    last_error      text        NULL,
    delivered_at    timestamptz NULL,

    CONSTRAINT pk_harvest_subscription_event PRIMARY KEY (id),
    CONSTRAINT ux_harvest_subscription_event_event_id UNIQUE (event_id),
    CONSTRAINT fk_harvest_subscription_event_subscription FOREIGN KEY (subscription_id) REFERENCES harvest_subscriptions (id) ON DELETE CASCADE,
    CONSTRAINT ck_harvest_subscription_event_type CHECK (type IN ('post.new', 'post.changed', 'post.extracted', 'link.state',
        'pipeline.run', 'pipeline.health', 'post.merged', 'post.unmerged', 'post.purged', 'post.relisted')),
    CONSTRAINT ck_harvest_subscription_event_reason CHECK (reason IS NULL OR reason IN ('ingest', 'backfill', 'resync')),
    CONSTRAINT ck_harvest_subscription_event_delivery CHECK (delivery_state IN ('pending', 'sending', 'retrying', 'delivered',
        'dead', 'skipped'))
);

CREATE INDEX ix_harvest_subscription_event_cursor ON harvest_subscription_event (subscription_id, tx, id);
CREATE INDEX ix_harvest_subscription_event_push ON harvest_subscription_event (next_attempt_at)
    WHERE delivery_state IN ('pending', 'retrying');
CREATE INDEX ix_harvest_subscription_event_post ON harvest_subscription_event (subscription_id, post_key, id)
    WHERE post_key IS NOT NULL;
