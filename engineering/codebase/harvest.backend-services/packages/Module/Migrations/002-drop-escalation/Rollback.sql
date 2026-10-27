-- Inverts 002-drop-escalation: restores the rung, lease and egress columns empty, and the wider checks.
-- Dev/test recovery only: the dropped values and the deleted meter rows stay gone.

ALTER TABLE harvest_pipelines
    DROP CONSTRAINT ck_harvest_pipelines_pause,
    ADD CONSTRAINT ck_harvest_pipelines_pause CHECK (
        (state = 'paused') = (pause_reason IS NOT NULL)
        AND (pause_reason IS NULL OR pause_reason IN ('operator', 'budget_hard', 'permission', 'account', 'idle')));

ALTER TABLE harvest_link_check_log
    ADD COLUMN rung text NULL;

ALTER TABLE harvest_captures
    ADD COLUMN rung text NULL;

ALTER TABLE harvest_usage
    DROP CONSTRAINT ck_harvest_usage_meter,
    ADD COLUMN rung text NULL,
    ADD CONSTRAINT ck_harvest_usage_meter CHECK (meter IN ('requests', 'bytes', 'egress_bytes', 'egress_ip_months',
        'solves', 'unlocker_requests', 'render_seconds', 'provider_results', 'api_units', 'llm_tokens'));

ALTER TABLE harvest_host_lane
    ADD COLUMN rung                 text          NOT NULL DEFAULT 'R0',
    ADD COLUMN rung_since           timestamptz   NOT NULL DEFAULT now(),
    ADD COLUMN probe_due_at         timestamptz   NULL,
    ADD COLUMN cost_per_success_usd numeric(12,6) NULL,
    ADD CONSTRAINT ck_harvest_host_lane_rung CHECK (rung IN ('R0', 'R1', 'R2', 'R3', 'R4', 'R5', 'R6', 'R7', 'R8'));

ALTER TABLE harvest_host_state
    ADD COLUMN egress text NULL;

ALTER TABLE harvest_work_item
    DROP CONSTRAINT ck_harvest_work_item_route,
    ADD COLUMN rung             text NULL,
    ADD COLUMN egress_lease_id  uuid NULL,
    ADD COLUMN session_lease_id uuid NULL,
    ADD CONSTRAINT ck_harvest_work_item_route CHECK (route IS NULL
        OR route IN ('static', 'render', 'api', 'provider', 'account')),
    ADD CONSTRAINT ck_harvest_work_item_rung CHECK (rung IS NULL
        OR rung IN ('R0', 'R1', 'R2', 'R3', 'R4', 'R5', 'R6', 'R7', 'R8'));
