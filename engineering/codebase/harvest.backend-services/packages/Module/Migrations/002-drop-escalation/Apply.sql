-- Drops the escalation ladder. Harvest records a challenge, a block or a rate limit as an outcome and backs the host
-- off; it never climbs to another route to get past one. The rung, lease and egress columns go, and so do the meters
-- only paid rungs wrote. `harvest_host_lane` stays: it holds each (host, lane)'s pause, failures, last challenge and
-- concurrency, the per-lane back-off state. Operator accounts left with the session leases, so `account` is no longer
-- a route or a pause reason.

ALTER TABLE harvest_work_item
    DROP CONSTRAINT ck_harvest_work_item_rung,
    DROP CONSTRAINT ck_harvest_work_item_route,
    DROP COLUMN rung,
    DROP COLUMN egress_lease_id,
    DROP COLUMN session_lease_id,
    ADD CONSTRAINT ck_harvest_work_item_route CHECK (route IS NULL OR route IN ('static', 'render', 'api', 'provider'));

ALTER TABLE harvest_host_state
    DROP COLUMN egress;

ALTER TABLE harvest_host_lane
    DROP CONSTRAINT ck_harvest_host_lane_rung,
    DROP COLUMN rung,
    DROP COLUMN rung_since,
    DROP COLUMN probe_due_at,
    DROP COLUMN cost_per_success_usd;

DELETE FROM harvest_usage WHERE meter IN ('egress_bytes', 'egress_ip_months', 'solves', 'unlocker_requests');

ALTER TABLE harvest_usage
    DROP CONSTRAINT ck_harvest_usage_meter,
    DROP COLUMN rung,
    ADD CONSTRAINT ck_harvest_usage_meter CHECK (meter IN ('requests', 'bytes', 'render_seconds', 'provider_results',
        'api_units', 'llm_tokens'));

ALTER TABLE harvest_captures
    DROP COLUMN rung;

ALTER TABLE harvest_link_check_log
    DROP COLUMN rung;

-- A pipeline paused for its account waits for an operator instead.
UPDATE harvest_pipelines SET pause_reason = 'operator' WHERE pause_reason = 'account';

ALTER TABLE harvest_pipelines
    DROP CONSTRAINT ck_harvest_pipelines_pause,
    ADD CONSTRAINT ck_harvest_pipelines_pause CHECK (
        (state = 'paused') = (pause_reason IS NOT NULL)
        AND (pause_reason IS NULL OR pause_reason IN ('operator', 'budget_hard', 'permission', 'idle')));
